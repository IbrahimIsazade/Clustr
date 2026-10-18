FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY src/Clustr/Clustr.csproj src/Clustr/
RUN dotnet restore src/Clustr/Clustr.csproj
COPY src/Clustr/ src/Clustr/
RUN dotnet publish src/Clustr/Clustr.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir -p /data && chown -R app:app /data
USER app
ENV ASPNETCORE_HTTP_PORTS=8080 DataDirectory=/data
EXPOSE 8080
ENTRYPOINT ["dotnet", "Clustr.dll"]
