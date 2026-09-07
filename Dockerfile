# syntax=docker/dockerfile:1

# ============================================================
# Build stage — restores and publishes the app.
# The compiled CSS (wwwroot/css/*.css) is already checked into
# the repo, so this build does NOT need Node/npm — just the .NET SDK.
# If you change tailwind-input.css or site.css, rebuild them locally
# first (npm run build:tailwind && npm run build:css) before building
# this image; see package.json.
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the project file first so `dotnet restore` is cached in its own
# layer and only re-runs when dependencies actually change.
COPY ExamPortal.csproj .
RUN dotnet restore ExamPortal.csproj

# Now copy everything else and publish.
COPY . .
RUN dotnet publish ExamPortal.csproj -c Release -o /app/publish --no-restore

# ============================================================
# Runtime stage — smaller ASP.NET runtime image, no SDK.
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# App_Data/uploads is where FileStorageService writes candidate uploads
# (resumes, documents, offer letters) when FileStorage:PrivateRoot is
# unset (see appsettings.json), and FileStorageService.EnsureDirectoriesExist()
# creates it itself on startup — it has to already be writable by the
# non-root "app" user this image runs as, or that first write fails.
RUN mkdir -p /app/App_Data && chown -R app:app /app/App_Data

# Run as the non-root user already present in the base image instead
# of root, for defense in depth.
USER app

COPY --from=build --chown=app:app /app/publish .

VOLUME ["/app/App_Data"]

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ExamPortal.dll"]
