FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["api-simulador-habitacional/api-simulador-habitacional.csproj", "api-simulador-habitacional/"]
RUN dotnet restore "./api-simulador-habitacional/api-simulador-habitacional.csproj"
COPY . .
WORKDIR "/src/api-simulador-habitacional"
RUN dotnet build "./api-simulador-habitacional.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./api-simulador-habitacional.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "api-simulador-habitacional.dll"]