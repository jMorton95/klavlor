ARG VERSION=10.0-alpine

FROM mcr.microsoft.com/dotnet/aspnet:$VERSION AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:$VERSION AS build
WORKDIR /src

RUN apk add --no-cache nodejs npm

COPY ["KlavLor.Domain/KlavLor.Domain.csproj", "KlavLor.Domain/"]
COPY ["KlavLor.Application/KlavLor.Application.csproj", "KlavLor.Application/"]
COPY ["KlavLor.Infrastructure/KlavLor.Infrastructure.csproj", "KlavLor.Infrastructure/"]
COPY ["KlavLor.Web/KlavLor.Web.csproj", "KlavLor.Web/"]

RUN dotnet restore "KlavLor.Web/KlavLor.Web.csproj"

COPY ["package.json", "package-lock.json*", "./"]
RUN npm install

COPY . .

WORKDIR "/src/KlavLor.Web"

RUN dotnet build "KlavLor.Web.csproj" -c Release -o /app/build
RUN npx tailwindcss \
    -i wwwroot/app.css \
    -o wwwroot/styles.css \
    --minify

FROM build AS publish
RUN dotnet publish "KlavLor.Web.csproj" -c Release -o /app/publish -p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "KlavLor.Web.dll"]
