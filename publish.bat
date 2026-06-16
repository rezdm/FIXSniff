rm -rf bin
rm -rf obj

dotnet publish ^
 -c Release ^
 -r win-x64 ^
 --self-contained true ^
 -p:PublishSinglefile=true ^
 -p:IncludeNativeLibrariesForSelfExtract=true ^
 -p:PublishReadyToRun=true ^
 -p:EnableCompressionInSingleFile=true ^
 -p:DebugType=none ^
 -o publish

