# SparseLattice.Test / TestData / Embeddings

This directory is the drop-zone for real embedding files used in identity-of-function
testing. It is intentionally empty in the repository (the files are too large to commit).

## File format

Each file is a CSV with one embedding vector per row and one float value per column:

```
0.012345,-0.98765,0.00123,...
-0.45678,0.56789,-0.01234,...
```

- No header row.
- Values are plain decimal floats (dot as decimal separator, invariant culture).
- All rows must have the same number of columns (the embedding dimension).

## File naming convention

```
{modelName}_{dimensions}.csv
```

Examples:
- `nomic-embed-text_768.csv`
- `all-MiniLM-L6-v2_384.csv`
- `mxbai-embed-large_1024.csv`

## How to generate these files

### Using Ollama (recommended for quick testing)

Run the helper script (PowerShell):

```powershell
# embed-corpus.ps1
$model = "nomic-embed-text"
$texts = Get-Content .\sample-corpus.txt   # one text per line
$out = @()
foreach ($text in $texts) {
    $body = @{ model=$model; input=$text } | ConvertTo-Json
    $resp = Invoke-RestMethod -Uri "http://localhost:11434/api/embed" -Method Post -Body $body -ContentType "application/json"
    $out += ($resp.embeddings[0] -join ",")
}
$out | Set-Content ".\${model}_$($resp.embeddings[0].Count).csv"
```

### From any Python embedding pipeline

```python
import numpy as np
from sentence_transformers import SentenceTransformer

model = SentenceTransformer("all-MiniLM-L6-v2")
texts = open("corpus.txt").read().splitlines()
vecs = model.encode(texts)  # shape (N, D)
np.savetxt(f"all-MiniLM-L6-v2_{vecs.shape[1]}.csv", vecs, delimiter=",")
```

## Minimum corpus size

Tests require at least 5 vectors to run meaningfully. For the threshold sweep tests,
20+ vectors are recommended to avoid recall being dominated by sampling noise.

## Security note

Files placed here are never loaded into any model or executed — they are read as plain
float data only.
