FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY ["HelloWorldWeb.csproj", "./"]
RUN dotnet restore "HelloWorldWeb.csproj"

# Copy everything else and build
COPY . .
RUN dotnet build "HelloWorldWeb.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "HelloWorldWeb.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app

# Create data directory for local storage (fallback)
RUN mkdir -p /app/data

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "HelloWorldWeb.dll"]
