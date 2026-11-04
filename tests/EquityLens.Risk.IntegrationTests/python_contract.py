"""Consume freshly serialized C# input and emit real Python engine output over stdio."""
import hashlib
import json
import sys

from equitylens_mathematics.engine import calculate_backtest, calculate_current_risk
from equitylens_mathematics.models import RiskBacktestInput

raw = sys.stdin.read()
model = RiskBacktestInput.model_validate_json(raw)
input_hash = hashlib.sha256(raw.encode()).hexdigest()
# No generated expected answers: these are outputs from the production functions.
json.dump({
    "inputHash": input_hash,
    "validatedInput": model.model_dump(mode="json", by_alias=True),
    "backtest": calculate_backtest(model, input_hash),
    "risk": calculate_current_risk(model, input_hash, "risk"),
}, sys.stdout, ensure_ascii=False, allow_nan=False)
