using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using KlavLor.Application.Common.Settings;
using KlavLor.Domain.Entities;
using KlavLor.Domain.Interfaces.Services;

namespace KlavLor.Infrastructure.Persistence.EntityFramework;

public interface IMigrationService
{
    Task ApplyStartupDatabaseMigrations();
}

internal sealed class EntityFrameworkMigrator(
    DataContext dataContext,
    IOptions<SystemConfiguration> systemConfiguration,
    ILogger<EntityFrameworkMigrator> logger,
    IPasswordService passwordService) : IMigrationService
{
    public async Task ApplyStartupDatabaseMigrations()
    {
        try
        {
            await dataContext.Database.MigrateAsync();

            await EnsureSystemUserExists();
            await EnsureSeedDataExists();
        }
        catch(Exception ex)
        {
            logger.LogCritical(ex, "Unable to connect to the database.");
            throw;
        }
    }

    private async Task EnsureSystemUserExists()
    {
        var systemConfigurationValues = systemConfiguration.Value;

        if (string.IsNullOrWhiteSpace(systemConfigurationValues.SystemUsername) || string.IsNullOrWhiteSpace(systemConfigurationValues.SystemPassword))
            return;

        if (await dataContext.Users.FirstOrDefaultAsync(u => u.Email == systemConfigurationValues.SystemUsername) is not null)
            return;

        var roles = await dataContext.Roles.ToListAsync();

        var systemUser = new User("System", "User", systemConfigurationValues.SystemUsername, true);

        foreach (var role in roles)
        {
            systemUser.AssignRole(role);
        }

        systemUser.HashedPassword = passwordService.HashPassword(systemUser, systemConfigurationValues.SystemPassword);

        dataContext.Users.Add(systemUser);
        await dataContext.SaveChangesAsync();
    }

    private async Task EnsureSeedDataExists()
    {
        if (await dataContext.Templates.AnyAsync())
            return;

        var systemUser = await dataContext.Users.FirstOrDefaultAsync();
        if (systemUser is null)
            return;

        const string img = "https://oldschool.runescape.wiki/images/";

        var template = new Template("Ironman Gear Progression", "Comprehensive ironman account gear and unlock progression", systemUser.Id)
        {
            IsPublic = true
        };

        var groups = new (string Label, NodeType Type)[][]
        {
            // 1
            [("Amulet of strength", NodeType.Item), ("Climbing boots", NodeType.Item), ("Rune pouch", NodeType.Item)],
            // 2
            [("Iban's staff (u)", NodeType.Item), ("Protect from Melee", NodeType.Prayer), ("Ancient staff", NodeType.Item), ("Eagle Eye", NodeType.Prayer)],
            // 3
            [("Fighter torso", NodeType.Item), ("Granite body", NodeType.Item)],
            // 4
            [("Dragon scimitar", NodeType.Item), ("Berserker ring (i)", NodeType.Item), ("Herb sack", NodeType.Item)],
            // 5
            [("Dragon defender", NodeType.Item), ("Barrows gloves", NodeType.Item)],
            // 6
            [("Helm of neitiznot", NodeType.Item), ("Book of the dead", NodeType.Item), ("Ava's accumulator", NodeType.Item), ("Piety", NodeType.Prayer), ("Gem bag", NodeType.Item)],
            // 7
            [("Hallowed crystal shard", NodeType.Item)],
            // 8
            [("Dark altar", NodeType.Construction), ("Spirit tree", NodeType.Construction), ("Rejuvenation pool", NodeType.Construction), ("Basic jewellery box", NodeType.Construction)],
            // 9
            [("Arkan blade", NodeType.Item)],
            // 10
            [("Black mask (i)", NodeType.Item), ("Imbued zamorak cape", NodeType.Item), ("Bonecrusher", NodeType.Item), ("Ash sanctifier", NodeType.Item), ("Bigger and Badder", NodeType.Slayer), ("Ice Barrage", NodeType.Spell), ("Infinity boots", NodeType.Item), ("Mage's book", NodeType.Item)],
            // 11
            [("Mixed hide cape", NodeType.Item), ("Mixed hide boots", NodeType.Item), ("Red chinchompa", NodeType.Item), ("70 Ranged", NodeType.Skill)],
            // 12
            [("Broader Fletching", NodeType.Slayer), ("69 Slayer", NodeType.Skill)],
            // 13
            [("Ghommal's hilt 2", NodeType.Item)],
            // 14
            [("Elite void top", NodeType.Item), ("Elite void robe", NodeType.Item), ("Void ranger helm", NodeType.Item), ("Void knight gloves", NodeType.Item), ("Crystal halberd", NodeType.Item)],
            // 15
            [("92 Ranged", NodeType.Skill), ("86 Strength", NodeType.Skill)],
            // 16
            [("Crystal body", NodeType.Item), ("Crystal legs", NodeType.Item), ("Crystal helm", NodeType.Item), ("Bow of faerdhinen (c)", NodeType.Item)],
            // 17
            [("Spellbook Swap", NodeType.Spell)],
            // 18
            [("Explorer's ring 4", NodeType.Item), ("Wrath rune", NodeType.Item), ("Fire cape", NodeType.Item), ("Karamja gloves 3", NodeType.Item), ("Amulet of glory", NodeType.Item)],
            // 19
            [("Pharaoh's sceptre", NodeType.Item), ("Karamja gloves 4", NodeType.Item)],
            // 20
            [("Occult altar", NodeType.Construction), ("Ornate jewellery box", NodeType.Construction), ("Fairy ring", NodeType.Construction), ("Ornate rejuvenation pool", NodeType.Construction)],
            // 21
            [("Bloodbark body", NodeType.Item), ("Bloodbark legs", NodeType.Item), ("Bloodbark helm", NodeType.Item), ("Ava's assembler", NodeType.Item)],
            // 22
            [("Ancient icon", NodeType.Item)],
            // 23
            [("Slayer helmet (i)", NodeType.Item), ("Arclight", NodeType.Item), ("Warped sceptre", NodeType.Item), ("Reptile Got Ripped", NodeType.Slayer), ("Greater Challenge", NodeType.Slayer)],
            // 24
            [("Prescription goggles", NodeType.Item), ("Alchemist's amulet", NodeType.Item)],
            // 25
            [("Amulet of torture", NodeType.Item), ("Necklace of anguish", NodeType.Item), ("Zamorakian hasta", NodeType.Item), ("Bandos godsword", NodeType.Item)],
            // 26
            [("Voidwaker", NodeType.Item), ("Dragon pickaxe", NodeType.Item)],
            // 27
            [("Deadeye", NodeType.Prayer), ("Mystic Vigour", NodeType.Prayer)],
            // 28
            [("Thread of elidinis", NodeType.Item), ("Lightbearer", NodeType.Item)],
            // 29
            [("Ghommal's hilt 4", NodeType.Item)],
            // 30
            [("Abyssal whip", NodeType.Item), ("Abyssal tentacle", NodeType.Item), ("Dragon warhammer", NodeType.Item), ("Emberlight", NodeType.Item), ("Dragon boots", NodeType.Item), ("Scorching bow", NodeType.Item), ("Tormented bracelet", NodeType.Item), ("Burning claws", NodeType.Item), ("Ring of suffering (i)", NodeType.Item)],
            // 31
            [("Avernic treads", NodeType.Item), ("Rite of vile transference", NodeType.Prayer)],
            // 32
            [("Desert amulet 4", NodeType.Item), ("Rada's blessing 4", NodeType.Item)],
            // 33
            [("Primordial boots", NodeType.Item), ("Amulet of rancour", NodeType.Item), ("Eternal boots", NodeType.Item), ("Occult necklace", NodeType.Item)],
            // 34
            [("Eye of ayak", NodeType.Item), ("Confliction gauntlets", NodeType.Item)],
            // 35
            [("Toxic blowpipe", NodeType.Item)],
            // 36
            [("Osmumten's fang", NodeType.Item)],
            // 37
            [("Ferocious gloves", NodeType.Item)],
            // 38
            [("Rigour", NodeType.Prayer), ("Augury", NodeType.Prayer)],
            // 39
            [("Infernal cape", NodeType.Item)],
            // 40
            [("Oathplate chest", NodeType.Item), ("Oathplate legs", NodeType.Item), ("Oathplate helm", NodeType.Item)],
            // 41
            [("Ultor ring", NodeType.Item)],
            // 42
            [("Dizana's quiver", NodeType.Item)],
            // 43
            [("Avernic defender", NodeType.Item), ("Scythe of vitur", NodeType.Item)],
            // 44
            [("Tumeken's shadow", NodeType.Item), ("Magus ring", NodeType.Item)],
            // 45
            [("Twisted bow", NodeType.Item), ("Dragon claws", NodeType.Item), ("Ancestral robe top", NodeType.Item), ("Ancestral robe bottom", NodeType.Item), ("Ancestral hat", NodeType.Item), ("Elder maul", NodeType.Item)],
            // 46
            [("Masori body (f)", NodeType.Item), ("Masori chaps (f)", NodeType.Item), ("Masori mask (f)", NodeType.Item), ("Elidinis' ward", NodeType.Item)],
            // 47
            [("Saturated heart", NodeType.Item), ("Venator bow", NodeType.Item)],
            // 48
            [("Ghommal's hilt 5", NodeType.Item)],
            // 49
            [("Torva platebody", NodeType.Item), ("Torva platelegs", NodeType.Item), ("Torva full helm", NodeType.Item), ("Zaryte crossbow", NodeType.Item), ("Zaryte vambraces", NodeType.Item)],
        };

        const double groupSpacing = 200;
        const double itemSpacing = 70;
        const double centerY = 350;
        const double startX = 100;

        var templateGroups = new List<(TemplateNodeGroup Group, List<TemplateNode> Nodes)>();

        for (var g = 0; g < groups.Length; g++)
        {
            var items = groups[g];
            var posX = startX + g * groupSpacing;
            var posY = centerY - (items.Length - 1) * itemSpacing / 2.0;

            // Create a group container
            var group = template.AddGroup(posX, posY);

            var groupNodes = new List<TemplateNode>();
            for (var i = 0; i < items.Length; i++)
            {
                var (label, type) = items[i];
                var iconUrl = GetSeedIconUrl(label, type, img);
                // Node position is unused when grouped, but set a reasonable default
                var node = template.AddNode(label, type, posX + 10, posY + i * 28 + 6, iconUrl: iconUrl);
                groupNodes.Add(node);
            }

            templateGroups.Add((group, groupNodes));
        }

        dataContext.Templates.Add(template);
        await dataContext.SaveChangesAsync();

        // Assign nodes to their groups (now that IDs are generated)
        foreach (var (group, nodes) in templateGroups)
        {
            foreach (var node in nodes)
            {
                template.AssignNodeToGroup(node.Id, group.Id);
            }
        }

        // Add one representative edge between consecutive groups
        for (var g = 0; g < templateGroups.Count - 1; g++)
        {
            var fromNode = templateGroups[g].Nodes[0];
            var toNode = templateGroups[g + 1].Nodes[0];
            template.AddEdge(fromNode.Id, toNode.Id);
        }

        await dataContext.SaveChangesAsync();

        logger.LogInformation("Seed template created successfully.");
    }

    private static string GetSeedIconUrl(string label, NodeType type, string img)
    {
        if (type == NodeType.Skill)
        {
            // "70 Ranged" → "Ranged_icon.png"
            var skillName = label.Split(' ')[^1];
            return $"{img}{skillName}_icon.png";
        }

        return $"{img}{label.Replace(" ", "_")}.png";
    }
}
