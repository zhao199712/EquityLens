from __future__ import annotations

from datetime import date

from pydantic import BaseModel, ConfigDict, Field, field_validator, model_validator


class RiskBacktestInput(BaseModel):
    model_config = ConfigDict(alias_generator=lambda value: value[0].lower() + value[1:], populate_by_name=True)

    portfolio_id: str = Field(alias="portfolioId")
    from_date: date = Field(alias="from")
    to_date: date = Field(alias="to")
    lookback_days: int = Field(alias="lookbackDays", gt=1)
    simulations: int = Field(gt=0, le=100_000)
    confidence_levels: list[float] = Field(alias="confidenceLevels", min_length=1)
    ewma_lambda: float = Field(alias="ewmaLambda", gt=0, lt=1)
    shrinkage_alpha: float = Field(alias="shrinkageAlpha", ge=0, le=1)
    conservative_residual_cap_quantile: float = Field(
        alias="conservativeResidualCapQuantile", ge=0, lt=1
    )
    return_dates: list[date] = Field(alias="returnDates")
    portfolio_returns: list[float] = Field(alias="portfolioReturns")
    asset_returns: list[list[float]] = Field(alias="assetReturns", min_length=1)
    weights: list[float] = Field(min_length=1)

    @field_validator("confidence_levels")
    @classmethod
    def validate_confidence_levels(cls, values: list[float]) -> list[float]:
        if any(value <= 0 or value >= 1 for value in values):
            raise ValueError("confidence levels must be between zero and one")
        return values

    @model_validator(mode="after")
    def validate_dimensions(self) -> "RiskBacktestInput":
        count = len(self.portfolio_returns)
        if len(self.return_dates) != count:
            raise ValueError("return dates and portfolio returns must have equal lengths")
        if len(self.asset_returns) != len(self.weights):
            raise ValueError("asset return rows and weights must have equal lengths")
        if any(len(row) != count for row in self.asset_returns):
            raise ValueError("all asset return rows must have equal lengths")
        if count <= self.lookback_days:
            raise ValueError("return history must exceed the lookback window")
        if any(weight < 0 for weight in self.weights) or sum(self.weights) <= 0:
            raise ValueError("weights must be non-negative and have a positive sum")
        return self
