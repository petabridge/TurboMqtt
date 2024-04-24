# PowerShell script to run 5 instances of a process with 'dotnet run -c Release'
# and terminate all instances if the script is stopped with Control+C

# Array to keep track of process objects
$processes = @()

# Register an event handler to respond to process exit/interruption
$exitEvent = Register-ObjectEvent -InputObject $([System.Console]) -EventName "CancelKeyPress" -Action {
    Write-Host "Stopping all started processes..."
    global:processes | ForEach-Object {
        $_ | Stop-Process -Force
    }
    Unregister-Event -SourceIdentifier $exitEvent.SourceIdentifier
    exit
}

try {
    # Loop to start 5 instances
    for ($i = 0; $i -lt 5; $i++) {
        $process = Start-Process -PassThru -NoNewWindow -FilePath "dotnet" -ArgumentList "run -c Release --no-build"
        $processes += $process
        Write-Host "Started instance $i with PID $($process.Id)"
    }

    Write-Host "All instances started. Press Control+C to stop all and exit."

    # Keep script running in the foreground
    while ($true) {
        Start-Sleep -Seconds 10
    }
}
catch {
    # If the script exits for any reason, attempt to clean up
    $processes | ForEach-Object {
        if ($_ -ne $null) {
            $_ | Stop-Process -Force -ErrorAction SilentlyContinue
        }
    }
}
finally {
    # Cleanup code to unregister the event and ensure processes are stopped
    Unregister-Event -SourceIdentifier $exitEvent.SourceIdentifier
    $processes | ForEach-Object {
        $_ | Stop-Process -Force -ErrorAction SilentlyContinue
    }
}
