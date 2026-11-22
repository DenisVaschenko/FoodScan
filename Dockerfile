FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY Directory.Build.props global.json ./
COPY src/FoodScan.Api/FoodScan.Api.csproj src/FoodScan.Api/
COPY src/FoodScan.Api/packages.lock.json src/FoodScan.Api/
RUN dotnet restore src/FoodScan.Api/FoodScan.Api.csproj --locked-mode
COPY src/ src/
RUN dotnet publish src/FoodScan.Api/FoodScan.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:11.0
USER root
RUN apt-get update && apt-get install -y --no-install-recommends tesseract-ocr tesseract-ocr-ukr tesseract-ocr-eng && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
USER app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "FoodScan.Api.dll"]
