# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY OrderRouting.sln ./
COPY src/OrderRouting.Api/OrderRouting.Api.csproj src/OrderRouting.Api/
COPY tests/OrderRouting.UnitTests/OrderRouting.UnitTests.csproj tests/OrderRouting.UnitTests/
COPY tests/OrderRouting.IntegrationTests/OrderRouting.IntegrationTests.csproj tests/OrderRouting.IntegrationTests/
COPY tools/OrderRouting.Diagnostics/OrderRouting.Diagnostics.csproj tools/OrderRouting.Diagnostics/
RUN dotnet restore OrderRouting.sln

COPY . .
RUN dotnet publish src/OrderRouting.Api/OrderRouting.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish ./
COPY service_data ./service_data

ENV ASPNETCORE_URLS=http://+:8080
ENV Data__ProductsPath=service_data/products.csv
ENV Data__SuppliersPath=service_data/suppliers.csv
ENV Routing__LocalRatingSimilarityDelta=0.5
ENV Routing__MaxQueuedRequests=100

EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "OrderRouting.Api.dll"]
