import os
import sys
import tempfile
from pathlib import Path

# Testes e módulos importam a partir da pasta Social/.
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))

# app.py cria a aplicação padrão ao ser importado: aponta para uma pasta temporária.
os.environ.setdefault("SOCIAL_DATA_DIR", tempfile.mkdtemp(prefix="social-tests-"))
