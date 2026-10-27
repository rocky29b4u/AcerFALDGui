@echo off
rem Start the FALD sync engine in the background (minimized console).
start "" /min "%~dp0FaldSync\bin\Release\net8.0-windows\FaldSync.exe" watch
