@echo off
pushd "%~dp0"
powershell -ExecutionPolicy Bypass -File "%~dp0run-bot.ps1"
popd
