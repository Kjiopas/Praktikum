# Translation helper

This folder contains a small Python utility that can translate your ASP.NET `.resx` resource file into:

- Turkish (`tr`)
- English (`en`, source)
- Bulgarian (`bg`)

## Install

```bash
python3 -m pip install -r tools/requirements.txt
```

## Generate translated `.resx` files

From the repo root:

```bash
python3 tools/translate_resx.py \
  --input "LumiSense/LumiSense/Resources/SharedResource.resx" \
  --from en \
  --to tr bg \
  --overwrite
```

This will create:

- `SharedResource.tr.resx`
- `SharedResource.bg.resx`

in the same `Resources/` folder (unless you pass `--out-dir`).

