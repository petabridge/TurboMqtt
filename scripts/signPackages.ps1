param(
    [string]$ConfigPath,
    [string]$UserName,
    [string]$Password,
    [string]$ProductName,
    [string]$ProductDescription,
    [string]$ProductUrl,
    [string]$DirectoryPath
)

# Logging the received parameters (optional, for debugging)
Write-Output "Using configuration: $ConfigPath"
Write-Output "Product Name: $ProductName"
Write-Output "Product Description: $ProductDescription"
Write-Output "Product URL: $ProductUrl"
Write-Output "Directory for signing: $DirectoryPath"

# Validate that the directory exists
if (-Not (Test-Path $DirectoryPath)) {
    Write-Error "Directory does not exist: $DirectoryPath"
    exit 1
}

# Loop over each .nupkg and .snupkg file in the directory
Get-ChildItem -Path $DirectoryPath -Filter *.nupkg,*.snupkg -Recurse | ForEach-Object {
    $filePath = $_.FullName

    Write-Output "Signing file: $filePath"

    # Command to execute SignClient for each file
    ./SignClient --config $ConfigPath `
        -r $UserName `
        -s $Password `
        -n $ProductName `
        -d $ProductDescription `
        -u $ProductUrl `
        -i $filePath
}