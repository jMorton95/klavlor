using KlavLor.Application.Features.Viewer.ToggleCompletion;
using KlavLor.Application.Features.Viewer.ViewerData;
using KlavLor.Application.Interfaces.Authentication;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Shared;
using KlavLor.Infrastructure.Persistence.EntityFramework;
using KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Completions;
using KlavLor.Infrastructure.Persistence.EntityFramework.Repositories.Templates;
using Microsoft.Extensions.Logging.Abstractions;

namespace KlavLor.IntegrationTests;

// COMPLETION IS THE TEMPLATE'S STATE, NOT THE CLICKER'S, and the read and the write used to
// disagree about that.
//
// ViewerDataHandler has always loaded the OWNER's rows on purpose, so every viewer of a template
// sees one shared progress state. ToggleCompletionHandler wrote against currentUser instead. For
// the owner the two ids are the same, so the mismatch was invisible; for an admin it was silent and
// total. The toggle succeeded, a row was written under the ADMIN's account, and the viewer then
// rendered the owner's rows, which had not changed - so the tick survived the htmx swap and
// disappeared on the next load. It was reported as "the admin cannot check nodes", when in fact it
// was checking a node that nothing displays. Notes went the same way.
//
// These tests pin the fix from both ends: an admin's tick must land on the OWNER's row and nothing
// must be written under the admin, because either half alone would still look right in the swap and
// wrong on reload.
[Collection("postgres")]
public sealed class AdminCompletionOnBehalfTests(PostgresFixture fx)
{
    private sealed class FakeCurrentUser(int? userId, bool isAdmin) : ICurrentUser
    {
        public int? UserId => userId;
        public bool IsAdmin => isAdmin;
        public bool IsInRole(RoleName roleName) =>
            roleName == RoleName.User || (isAdmin && roleName == RoleName.Admin);
    }

    private sealed record Rig(int OwnerId, int AdminId, int OutsiderId, int TemplateId, int NodeA, int NodeB);

    private async Task<Rig> Setup(string tag)
    {
        await using var ctx = fx.CreateContext();
        var (ownerId, _) = await Seed.UserAndCharacter(ctx, tag + "own");
        var (adminId, _) = await Seed.UserAndCharacter(ctx, tag + "adm");
        var (outsiderId, _) = await Seed.UserAndCharacter(ctx, tag + "out");

        var template = new Template(tag + " template", "seeded", ownerId) { IsPublic = false };
        ctx.Templates.Add(template);
        await ctx.SaveChangesAsync();

        var a = new TemplateNode { TemplateId = template.Id, Label = "A", NodeType = NodeType.Item, PositionX = 0, PositionY = 0 };
        var b = new TemplateNode { TemplateId = template.Id, Label = "B", NodeType = NodeType.Item, PositionX = 10, PositionY = 0 };
        ctx.TemplateNodes.AddRange(a, b);
        await ctx.SaveChangesAsync();

        return new Rig(ownerId, adminId, outsiderId, template.Id, a.Id, b.Id);
    }

    private (ToggleCompletionHandler Toggle, ViewerDataHandler Viewer, UserNodeCompletionRepository Completions)
        HandlersFor(DataContext ctx, int? userId, bool isAdmin)
    {
        var templates = new TemplateRepository(ctx, NullLogger<TemplateRepository>.Instance);
        var completions = new UserNodeCompletionRepository(ctx, NullLogger<UserNodeCompletionRepository>.Instance);
        var who = new FakeCurrentUser(userId, isAdmin);
        return (new ToggleCompletionHandler(templates, completions, who),
                new ViewerDataHandler(templates, completions, who),
                completions);
    }

    [Fact]
    public async Task An_admin_tick_is_written_against_the_owner_and_not_the_admin()
    {
        var rig = await Setup("acb1");
        await using var ctx = fx.CreateContext();
        var (toggle, _, completions) = HandlersFor(ctx, rig.AdminId, isAdmin: true);

        var result = await toggle.Handle(new ToggleCompletionCommand
        {
            TemplateId = rig.TemplateId,
            NodeId = rig.NodeA,
            Note = "filled in by the admin"
        });

        Assert.True(result.IsSuccess);

        var onOwner = await completions.GetCompletion(rig.OwnerId, rig.NodeA);
        Assert.NotNull(onOwner);
        Assert.Equal("filled in by the admin", onOwner!.Note);

        // The half that made this invisible: a row under the admin renders nowhere.
        Assert.Null(await completions.GetCompletion(rig.AdminId, rig.NodeA));
    }

    [Fact]
    public async Task What_the_admin_ticked_is_what_the_owner_then_sees()
    {
        // The reload path, which is where the old behaviour actually failed - the swapped-in
        // fragment looked right and the next page load dropped it.
        var rig = await Setup("acb2");

        await using (var ctx = fx.CreateContext())
        {
            var (toggle, _, _) = HandlersFor(ctx, rig.AdminId, isAdmin: true);
            Assert.True((await toggle.Handle(new ToggleCompletionCommand
            {
                TemplateId = rig.TemplateId, NodeId = rig.NodeB, Note = "answer: bandos tassets"
            })).IsSuccess);
        }

        await using (var ctx = fx.CreateContext())
        {
            var (_, viewer, _) = HandlersFor(ctx, rig.OwnerId, isAdmin: false);
            var view = await viewer.Handle(new ViewerDataQuery { TemplateId = rig.TemplateId });

            Assert.True(view.IsSuccess);
            Assert.True(view.Value!.CompletionDates.ContainsKey(rig.NodeB));
            Assert.Equal("answer: bandos tassets", view.Value.CompletionDates[rig.NodeB].Note);
        }
    }

    [Fact]
    public async Task An_admin_can_untick_what_the_owner_ticked()
    {
        // Toggle is symmetric, so acting on the owner's row has to work in both directions -
        // otherwise an admin could add a tick but never take one back.
        var rig = await Setup("acb3");

        await using (var ctx = fx.CreateContext())
        {
            var (toggle, _, _) = HandlersFor(ctx, rig.OwnerId, isAdmin: false);
            await toggle.Handle(new ToggleCompletionCommand { TemplateId = rig.TemplateId, NodeId = rig.NodeA });
        }

        await using (var ctx = fx.CreateContext())
        {
            var (toggle, _, completions) = HandlersFor(ctx, rig.AdminId, isAdmin: true);
            Assert.NotNull(await completions.GetCompletion(rig.OwnerId, rig.NodeA));

            await toggle.Handle(new ToggleCompletionCommand { TemplateId = rig.TemplateId, NodeId = rig.NodeA });

            Assert.Null(await completions.GetCompletion(rig.OwnerId, rig.NodeA));
        }
    }

    [Fact]
    public async Task The_owners_own_tick_still_lands_on_their_own_row()
    {
        // The ordinary path, unchanged - worth pinning because the fix moved the write target and
        // for the owner the two ids coincide, which is exactly what hid the bug in the first place.
        var rig = await Setup("acb4");
        await using var ctx = fx.CreateContext();
        var (toggle, _, completions) = HandlersFor(ctx, rig.OwnerId, isAdmin: false);

        Assert.True((await toggle.Handle(new ToggleCompletionCommand
        {
            TemplateId = rig.TemplateId, NodeId = rig.NodeA, Note = "mine"
        })).IsSuccess);

        var onOwner = await completions.GetCompletion(rig.OwnerId, rig.NodeA);
        Assert.NotNull(onOwner);
        Assert.Equal("mine", onOwner!.Note);
    }

    [Fact]
    public async Task A_signed_in_stranger_still_cannot_tick_someone_elses_template()
    {
        // Writing against the owner must not become a way for any authenticated user to edit any
        // template: the authorisation check is what limits the actors to the owner and admins.
        var rig = await Setup("acb5");
        await using var ctx = fx.CreateContext();
        var (toggle, _, completions) = HandlersFor(ctx, rig.OutsiderId, isAdmin: false);

        var result = await toggle.Handle(new ToggleCompletionCommand
        {
            TemplateId = rig.TemplateId, NodeId = rig.NodeA
        });

        Assert.False(result.IsSuccess);
        Assert.Null(await completions.GetCompletion(rig.OwnerId, rig.NodeA));
    }
}
