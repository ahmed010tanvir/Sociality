FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build-env
WORKDIR /app

# Copy csproj and restore as distinct layers
COPY  "Reactivities.sln" "Reactivities.sln"
COPY  "API/API.csproj" "API/API.csproj"
COPY  "Application/Application.csproj" "Application/Application.csproj"
COPY  "Domain/Domain.csproj" "Domain/Domain.csproj"
COPY  "Infrastructure/Infrastructure.csproj" "Infrastructure/Infrastructure.csproj"
COPY  "Persistence/Persistence.csproj" "Persistence/Persistence.csproj"


RUN dotnet restore "Reactivities.sln"

# Copy everything else and build
COPY . .
WORKDIR /app
RUN dotnet publish -c Release -o out

# Build runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build-env /app/out .
EXPOSE 80
ENTRYPOINT ["dotnet", "API.dll"]