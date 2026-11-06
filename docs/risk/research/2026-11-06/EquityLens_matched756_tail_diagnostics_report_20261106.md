# 同756日GJR/GARCH：VaR群聚與ES相關診斷

## 結論
主設定756/21下，不能把GJR較好的平均FZ分數解讀為「超損不再聚集」或「ES已校準」。等權95%的兩模型均有一階VaR違約相依訊號；等權99% GJR的連續違約轉移增加至3次，GARCH為2次。snapshot95%的GJR連續違約轉移較少，但單模型p按名目5%參考門檻由顯著變不顯著，不等於模型之間差異顯著。

ES相關DE欄只有觀察到的U均值、C1與C5統計量，沒有適用本研究的DE p值，不能給「通過／不通過」結論。部分C統計量下降也不足以證明ES超損時間相依改善。

## 範圍與核對
只讀既有v2的24個預測CSV，全部4860日（2007-01-18至2026-11-04）；756/21為主，756/5及756/63為敏感度；2模型×2情境×2權重×3頻率×3水準＝72格。日期與同情境同權重實際報酬逐一對齊。既有breaches、expected、breach_rate、Kupiec p、independence p及DE U/C1/C5全部逐浮點完全重現。無重估、無正式版變更。所有結果是同批已看過資料的事後探索，left/right不是獨立實驗。

## 主756/21：VaR率與一階違約群聚
每格N=4860；95%、97.5%、99%理論預期違約分別243、121.5、48.6次。p均為未多重校正的漸近參考值，非模型勝負檢定。n11為相鄰交易觀測都違約的轉移數；不是獨立事件數。

|情境|權重|模型|水準|違約次數|違約率%|UC p|ind p|n11|最長連續違約|
|---|---|---|---:|---:|---:|---:|---:|---:|---:|
|left|snapshot|GARCH|95|254|5.22634|0.4722|0.0197838|22|4|
|left|snapshot|GARCH|97.5|133|2.73663|0.297892|0.486941|5|2|
|left|snapshot|GARCH|99|55|1.13169|0.366182|0.158952|2|2|
|left|snapshot|GJR|95|253|5.20576|0.513137|0.425153|16|2|
|left|snapshot|GJR|97.5|134|2.7572|0.258449|0.256049|6|2|
|left|snapshot|GJR|99|55|1.13169|0.366182|0.656147|1|2|
|left|equal|GARCH|95|264|5.4321|0.172585|1.55187e-05|32|4|
|left|equal|GARCH|97.5|132|2.71605|0.341278|0.0376252|8|2|
|left|equal|GARCH|99|54|1.11111|0.44436|0.148288|2|2|
|left|equal|GJR|95|267|5.49383|0.119723|6.51876e-05|31|4|
|left|equal|GJR|97.5|131|2.69547|0.388631|0.0345543|8|2|
|left|equal|GJR|99|56|1.15226|0.297595|0.0299151|3|2|
|right|snapshot|GARCH|95|255|5.24691|0.433178|0.0216154|22|4|
|right|snapshot|GARCH|97.5|133|2.73663|0.297892|0.486941|5|2|
|right|snapshot|GARCH|99|55|1.13169|0.366182|0.158952|2|2|
|right|snapshot|GJR|95|253|5.20576|0.513137|0.425153|16|2|
|right|snapshot|GJR|97.5|134|2.7572|0.258449|0.256049|6|2|
|right|snapshot|GJR|99|55|1.13169|0.366182|0.656147|1|2|
|right|equal|GARCH|95|262|5.39095|0.21662|3.03506e-05|31|4|
|right|equal|GARCH|97.5|131|2.69547|0.388631|0.0922814|7|2|
|right|equal|GARCH|99|54|1.11111|0.44436|0.148288|2|2|
|right|equal|GJR|95|267|5.49383|0.119723|6.51876e-05|31|4|
|right|equal|GJR|97.5|132|2.71605|0.341278|0.0376252|8|2|
|right|equal|GJR|99|57|1.17284|0.2385|0.0330974|3|2|

等權97.5%：left GARCH/GJR ind p=0.0376252/0.0345543；right為0.0922814/0.0376252，並非所有情境都有同樣結論。snapshot95% GARCH→GJR的n11=22→16（左右皆同），但仍不能以p值分組宣稱GJR比較上顯著更好。UC只檢查總違約率；不拒絕UC不代表時間上獨立，更不代表模型校準已證實。

## 主756/21：ES相關累積PIT診斷（無p值）
連續均勻PIT下，H均值參考α/2為95%:0.025、97.5%:0.0125、99%:0.005。這是理論參考，不是本離散、滾動、多資產預測已校準的有限樣本期望。

|情境|權重|模型|水準|U欄＝平均H|C1|C5|
|---|---|---|---:|---:|---:|---:|
|left|snapshot|GARCH|95|0.0273218|6.71857|30.5477|
|left|snapshot|GARCH|97.5|0.0138691|6.10559|35.2918|
|left|snapshot|GARCH|99|0.00605967|8.39227|15.7569|
|left|snapshot|GJR|95|0.0276827|2.94631|16.2457|
|left|snapshot|GJR|97.5|0.0143473|3.17261|20.9595|
|left|snapshot|GJR|99|0.0059177|4.27765|6.00337|
|left|equal|GARCH|95|0.0271053|17.5646|46.4714|
|left|equal|GARCH|97.5|0.0134708|7.99752|38.594|
|left|equal|GARCH|99|0.00556379|17.6885|27.8561|
|left|equal|GJR|95|0.0272313|15.3444|36.6161|
|left|equal|GJR|97.5|0.0135909|10.5032|35.0467|
|left|equal|GJR|99|0.00561523|17.3119|24.483|
|right|snapshot|GARCH|95|0.0273185|6.67422|30.6961|
|right|snapshot|GARCH|97.5|0.0138593|6.13046|35.5076|
|right|snapshot|GARCH|99|0.00604527|8.47858|15.9036|
|right|snapshot|GJR|95|0.027684|2.9446|16.3063|
|right|snapshot|GJR|97.5|0.0143481|3.16604|20.9276|
|right|snapshot|GJR|99|0.0059177|4.27765|6.00337|
|right|equal|GARCH|95|0.0270276|17.6836|46.4204|
|right|equal|GARCH|97.5|0.0133909|8.14887|37.3323|
|right|equal|GARCH|99|0.00543416|18.985|26.4384|
|right|equal|GJR|95|0.02723|15.5623|37.0159|
|right|equal|GJR|97.5|0.0135786|10.5709|35.0219|
|right|equal|GJR|99|0.005607|17.5097|23.1657|

等權97.5%的C1反而上升：left GARCH→GJR為7.99752→10.5032，right為8.14887→10.5709；雖然C5下降，仍無可用DE p，不能宣稱時間相依改善。

## 定義與解讀界線
- 以報酬表示損失尾部：VaR二元違約I_t=1(actual_t < VaR_t)，嚴格小於，非actual < ES。n_ij計算I_(t−1)=i、I_t=j，總轉移4859個，不接首尾。
- α=1−confidence、x=ΣI、p̂=x/N。ℓ0=x log α+(N−x)log(1−α)，ℓ1=x log p̂+(N−x)log(1−p̂)，LRuc=2(ℓ1−ℓ0)，p參考χ²(1)右尾。使用xlogy使0 log 0按0處理。
- π0=n01/(n00+n01)、π1=n11/(n10+n11)、π=(n01+n11)/4859。獨立模型ℓnull=(n00+n10)log(1−π)+(n01+n11)logπ；一階Markov模型ℓalt=n00 log(1−π0)+n01 logπ0+n10 log(1−π1)+n11 logπ1。LRind=2(ℓalt−ℓnull)，p參考χ²(1)右尾。它只測一階二元違約相依，不涵蓋所有時滯或超損幅度；π1>π0方向才與正群聚相符。
- 新增探索性LRcc=LRuc+LRind，由N筆觀測的UC與N−1個轉移的ind相加，p以χ²(2)右尾作參考，與原v2兩種p分欄；不是DE的C，不是本次重新校準的檢定。所有p未多重校正、未對參數估計與滾動多資產完整流程做尺寸校準；稀少99%違約轉移尤其須謹慎。不得以多少格p>.05來挑模型。
- 預測PIT u_t為當日模擬報酬樣本中≤actual的比例（searchsorted side=right）。DE的H_t=(α−u_t)/α × 1(u_t≤α)，亦即(1/α)∫₀^α 1(u_t≤s) ds。它給更深入左尾的PIT更大權重，測累積尾部違約，不是VaR二元I_t、不是ES門檻違約、也不是實際金額超損殘差(actual−ES)。
- 現有DE_U_mean_diagnostic=平均H，並非標準化U_ES z統計量。令d_t=H_t−α/2，ρ_j=Σ_(t=j+1..N)d_t d_(t−j) / Σ_(t=1..N)d_t²，C_m=N Σ_(j=1..m)ρ_j²，m=1、5。中心化用理論α/2，不是樣本平均H，因此平均偏離也可能影響C；不能把C一概說成純群聚強度。C5包含1至5階，不只第5階。
- DE與ES有理論聯繫，但這裡是PIT累積尾部診斷，不能直接當成僅針對已輸出ES點預測的完整有效性檢定。原模組另有asymptotic/MC等函式，不代表v2有呼叫或其p可直接轉用。本次沒有生成DE常態／χ² p，也沒有把舊T1317單資產校準挪給T4860滾動多資產流程。

## 敏感度與可重現檔案
全部756/5、756/63與主設定的72格保存在diagnostics_all72.csv；diagnostics_primary24.csv為主設定24格。CSV提供原精度率／p、LRuc/LRind、新增LRcc、n00/n01/n10/n11、條件違約率、U/C1/C5、違約連續段及輸入路徑，沒有DE p數值欄。敏感度亦只描述，不另挑最有利頻率；不同p之顯著與否不能代替配對模型差異檢定。

來源（相對工作區）：
- risk_model_comparison_long9_v2/metrics_all.csv：原指標。
- risk_model_comparison_long9_v2/evaluate.py：匯入舊coverage及呼叫DE觀察統計的實際v2路徑。
- risk_model_comparison/evaluate.py，coverage：本次UC／ind精確公式。
- release_replay_source/EquityLens-release-replay/src/EquityLens.Mathematics/src/equitylens_mathematics/research/es_backtest.py，_du_escanciano_stats：DE精確實作。
- risk_model_comparison_long9_v2/run_comparison.py，searchsorted：PIT的離散右順位定義。
- evaluate.py：本次72格重算與嚴格一致性assert；build_report.py：本報告；manifest.json：來源SHA256及套件版本；artifact_hashes.json：交付檔案SHA256。

重跑：工作區執行 release-replay-venv/bin/python risk_model_comparison_matched756_diagnostics/evaluate.py，再以同Python執行build_report.py。只寫本新資料夾；未重新估計任何模型。
