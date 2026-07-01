# Ver a documentação oficial da Microsoft para mais detalhes sobre a imagem base de .NET
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Estágio de build do SDK do .NET
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["PaymentsAPI/PaymentsAPI.csproj", "PaymentsAPI/"]
RUN dotnet restore "PaymentsAPI/PaymentsAPI.csproj"
COPY . .
WORKDIR "/src/PaymentsAPI"
RUN dotnet build "PaymentsAPI.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Estágio de publicação da API
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "PaymentsAPI.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Estágio final para execução da aplicação
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PaymentsAPI.dll"]
