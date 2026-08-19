FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Fatora.API/Fatora.API.csproj Fatora.API/
COPY Fatora.BL/Fatora.BL.csproj Fatora.BL/
COPY Fatora.DAL/Fatora.DAL.csproj Fatora.DAL/
RUN dotnet restore Fatora.API/Fatora.API.csproj

COPY . .
RUN dotnet publish Fatora.API/Fatora.API.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Azure App Service for Linux (Custom Container) does not inject a $PORT
# env var the way Render does - it instead expects the container to listen
# on a fixed, known port, which the WEBSITES_PORT App Service setting (set
# in the Portal, not here) tells the platform's front end to route to. 8080
# baked in at build time, no shell wrapper needed to resolve it at runtime.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Fatora.API.dll"]
