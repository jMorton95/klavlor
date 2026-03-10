- // exec: ./pvt.infrastructure

- dotnet ef migrations add NewProperites -o Persistence/EntityFramework/Migrations --startup-project="../PVT.Worker/PVT.Worker.csproj"

- dotnet ef database update  --startup-project="../PVT.Worker/PVT.Worker.csproj"