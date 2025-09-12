#!/bin/bash
commit=false
for arg in "$@"; do
  if [ "$arg" == "--commit" ] || [ "$arg" == "-c" ]; then
    commit=true
    break
  fi
done

cd discord-file-scraper
dotnet run
cd ..
source="tools/discord-file-scraper/onnx"
destination="models"
newFiles=false

mkdir -p "$destination"
for file in "$source"/*; do
  if [ -f "$file" ]; then
    filename=$(basename "$file")
    if [ ! -f "$destination/$filename" ]; then
        mv "$file" "$destination/"
        echo "[*] Moved $filename"
        newFiles=true
    fi
  fi
done

if [ "$commit" = true ] && [ "$newFiles" = true ]; then
    git add "$destination"
    git commit -m "chore(models): re-scrape models"
    git pull
    git push
    echo "[*] Pushed new models to repo"
fi