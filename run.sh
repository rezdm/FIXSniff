#!/bin/sh
export AVALONIA_X11_NET_WM_BYPASS_COMPOSITOR=1
dotnet build
dotnet run

