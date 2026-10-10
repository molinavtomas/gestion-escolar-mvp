FROM node:24-alpine AS frontend-build
WORKDIR /src/frontend
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY backend/GestionEscolar.Api/GestionEscolar.Api.csproj backend/GestionEscolar.Api/
RUN dotnet restore backend/GestionEscolar.Api/GestionEscolar.Api.csproj
COPY backend/GestionEscolar.Api/ backend/GestionEscolar.Api/
RUN dotnet publish backend/GestionEscolar.Api/GestionEscolar.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production
COPY --from=api-build /app/publish ./
COPY --from=frontend-build /src/frontend/dist/frontend/browser ./wwwroot
EXPOSE 10000
ENTRYPOINT ["dotnet", "GestionEscolar.Api.dll"]
