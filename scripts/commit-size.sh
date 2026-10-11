#!/usr/bin/env bash
# Liczy linie w staged diffie (dodane + usuniete), pomijajac pliki generowane.
# Uzycie: commit-size.sh [limit]   — kod wyjscia 1, gdy limit przekroczony.
LIMIT="${1:-${MAX_COMMIT_LINES:-200}}"
IGNORE='(^|/)(packages\.lock\.json|\.gitignore)$'   # pliki generowane (.gitignore z dotnet new gitignore)

total=0
while IFS=$'\t' read -r added deleted path; do
  [[ "$added" == "-" ]] && continue                 # plik binarny
  [[ "$path" =~ $IGNORE ]] && continue
  total=$((total + added + deleted))
  printf '%5d  %s\n' "$((added + deleted))" "$path"
done < <(git diff --cached --numstat)

printf -- '-----\n%5d  razem (limit %d)\n' "$total" "$LIMIT"
if (( total > LIMIT )); then
  echo "Commit za duzy — podziel go na mniejsze." >&2
  exit 1
fi
