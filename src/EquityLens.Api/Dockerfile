FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY EquityLens.Api.csproj ./
RUN dotnet restore EquityLens.Api.csproj
COPY . .
RUN dotnet publish EquityLens.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN useradd --create-home --uid 10001 appuser
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:5034
USER appuser
EXPOSE 5034
CMD ["dotnet", "EquityLens.Api.dll"]
