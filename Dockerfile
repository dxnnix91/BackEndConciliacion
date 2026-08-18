# ---- Etapa 1: compilar y publicar ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Backend.csproj ./
RUN dotnet restore Backend.csproj

COPY . .
RUN dotnet publish Backend.csproj -c Release -o /app/publish --no-restore

# ---- Etapa 2: imagen final (solo el runtime, mucho más liviana) ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080
ENTRYPOINT ["dotnet", "Backend.dll"]