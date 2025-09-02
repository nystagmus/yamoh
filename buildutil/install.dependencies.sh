#!/bin/bash

wget https://github.com/dlemstra/Magick.NET.BuildDependencies/releases/download/build-binaries-2025-08-30/ghostscript-10.0.0-linux-x86_64.tgz
tar zxvf ghostscript-10.0.0-linux-x86_64.tgz
cp ghostscript-10.0.0-linux-x86_64/gs-1000-linux-x86_64 /usr/bin/gs
chmod 755 /usr/bin/gs