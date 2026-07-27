FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY EquityLens.Api.csproj ./
RUN dotnet restore EquityLens.Api.csproj
COPY . .
RUN dotnet publish EquityLens.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/* \
    && useradd --create-home --uid 10001 appuser
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5034
USER appuser
EXPOSE 5034
CMD ["dotnet", "EquityLens.Api.dll"]
