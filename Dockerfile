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

# Render (and most PaaS hosts) assign the listen port at runtime via $PORT -
# ASPNETCORE_URLS has to be set from it at container start, not baked in at build time.
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:$PORT exec dotnet Fatora.API.dll"]
