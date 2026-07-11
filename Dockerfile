FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY WordGameBff.sln ./
COPY Directory.Build.props global.json ./
COPY src/WordGameBff.Domain/ src/WordGameBff.Domain/
COPY src/WordGameBff.Application/ src/WordGameBff.Application/
COPY src/WordGameBff.Infrastructure/ src/WordGameBff.Infrastructure/
COPY src/WordGameBff.Api/ src/WordGameBff.Api/

RUN dotnet restore src/WordGameBff.Api/WordGameBff.Api.csproj
RUN dotnet publish src/WordGameBff.Api/WordGameBff.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "WordGameBff.Api.dll"]
