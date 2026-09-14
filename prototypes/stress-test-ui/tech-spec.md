# 睿見財析 — Technical Specification

## 依赖 (Dependencies)

### 运行时依赖

| 包名 | 版本 | 用途 |
|------|------|------|
| react | ^19.1 | UI 框架 |
| react-dom | ^19.1 | DOM 渲染 |
| react-router-dom | ^7.6 | 双页路由（/ 与 /financials） |
| three | ^0.175 | 粒子系统 WebGL 渲染 |
| gsap | ^3.13 | 动画时间轴控制、滚动触发、页面过渡 |
| splitting | ^1.1 | 字符级 DOM 拆分，配合粒子动画 |
| lenis | ^1.3 | 平滑滚动 |

### 开发依赖

| 包名 | 版本 | 用途 |
|------|------|------|
| vite | ^6.3 | 构建工具 |
| @vitejs/plugin-react | ^4.5 | React 支持 |
| typescript | ^5.8 | 类型系统 |
| @types/react | ^19.1 | React 类型 |
| @types/react-dom | ^19.1 | ReactDOM 类型 |
| @types/three | ^0.175 | Three.js 类型 |
| tailwindcss | ^4.1 | 样式框架 |
| @tailwindcss/vite | ^4.1 | Tailwind Vite 插件 |

---

## 组件清单

### 布局组件 (Layout)

| 组件 | 来源 | 复用 | 说明 |
|------|------|------|------|
| NavigationHeader | 自建 | 双页共用 | 品牌区+导航选项+页面标题+子标题，包含页面切换动画 |
| Footer | 自建 | 双页共用 | 简约页脚 |

### 页面组件 (Pages)

| 组件 | 来源 | 说明 |
|------|------|------|
| PortfolioPage | 自建 | 投资组合分析主页，浅色主题 |
| FinancialsPage | 自建 | 财报分析页，深色主题 |

### 可复用组件 (Shared)

| 组件 | 来源 | 复用 | 说明 |
|------|------|------|------|
| ParticleCanvas | 自建 | 双页共用 | Three.js 粒子凝聚动画系统，通过 props 切换配色主题（暖橙/暗红） |
| KPIGrid | 自建 | 双页共用 | KPI 卡片网格，通过 props 配置列数、数据和主题 |
| ScrollReveal | 自建 | 全局 | IntersectionObserver 封装，统一滚动触发动画（translateY+opacity） |
| LineChart | 自建 | 双页 | 纯 SVG 折线图，支持 stroke-dashoffset 绘制动画 |
| BarChart | 自建 | 财报页 | 纯 SVG 柱状图，支持从底部 scaleY 生长动画 |
| StackedBarChart | 自建 | 财报页 | 纯 SVG 堆叠柱状图 |
| DonutChart | 自建 | 组合页 | 纯 SVG 圆环图，支持扇区 hover 外扩 |
| ScatterPlot | 自建 | 组合页 | 纯 SVG 散点图，含效率前沿虚线 |
| RadarChart | 自建 | 财报页 | 纯 SVG 雷达图 |
| DataTable | 自建 | 双页 | 通用数据表格，支持交替行背景、hover 高亮、行级差动画 |

---

## 动画实现

| 动画 | 库 | 实现方式 | 复杂度 |
|------|------|----------|--------|
| 粒子凝聚动画（字符拆解+物理收束+发光拖尾） | Three.js + GSAP + splitting | splitting 拆分文本为字符节点；Three.js 创建粒子系统，每个字符对应一个粒子，赋予随机初速度和方向，GSAP 时间轴控制 3s ease-out 收束至目标位置；Canvas2D 拖尾绘制发光效果（主题色） | 🔒 High |
| 页面切换过渡（导航 hover blur + 点击下滑跳转） | GSAP | 鼠标进入时 GSAP 控制文字 translateY/blur/opacity；点击时 GSAP 时间轴控制文字下滑至 translateY(100%) 并淡出新页面标题 | Medium |
| 折线图 stroke-dashoffset 绘制 | GSAP | SVG path 的 stroke-dasharray/dashoffset 动画，GSAP 控制 2s ease-out | Low |
| 柱状图 scaleY 生长 | GSAP | SVG rect 的 scaleY(0→1) 从底部生长，transform-origin 设为底部，0.1s 级差 | Low |
| 数据点弹出 | GSAP | scale(0→1)，0.05s 级差 | Low |
| 滚动显示动画（全局） | GSAP ScrollTrigger | ScrollReveal 组件封装，IntersectionObserver 触发的 translateY(30→0)+opacity 动画，0.1s 级差 | Low |
| 表格行滑入 | GSAP ScrollTrigger | translateX(-10→0)，0.05s 级差 | Low |
| 即时报价脉冲高亮 | CSS transition | background-color 闪烁（主题色→透明），CSS transition 0.3s | Low |
| KPI 卡片 hover 放大 | CSS transition | scale(1.02)，CSS transition 0.3s | Low |
| 卡片/圆环 hover 双向联动 | React state | 悬浮状态提升至共同父组件，通过 state 同步表格行与圆环扇区的高亮 | Low |
| 页面标题模糊恢复 | GSAP | filter blur(8px→0)，1.2s cubic-bezier | Low |

---

## 状态与逻辑

### 路由架构

使用 react-router-dom，两个顶级路由：
- `/` → PortfolioPage（浅色主题）
- `/financials` → FinancialsPage（深色主题）

### 主题管理

不引入 Context Provider。每个页面组件内部通过硬编码的 className 和颜色值区分主题（浅色 `#F5F5F5` / 深色 `#0A0A0A`）。ParticleCanvas 组件通过 props 接收 `theme: 'warm' | 'red'` 以切换拖尾发光色。

### 跨组件交互

- **圆环-表格双向联动**：DonutChart 与 DataTable 共享父组件中的 `hoveredSegment` 状态。DonutChart 的扇区 hover 更新 state，DataTable 根据 state 高亮对应行；反之亦然。
- **筛选器状态**：收益走势图的时间范围筛选（1Y/6M/3M/1M/YTD）为局部状态，切换时重绘图表数据。

### 粒子系统架构

ParticleCanvas 封装 Three.js 渲染逻辑：
- 通过 `useRef` 持有 canvas 和 renderer
- 初始化时读取 splitting 拆分后的字符 DOM 位置，计算目标坐标
- 每个帧更新粒子位置（ease-out 衰减速率），在独立 Canvas2D 层绘制拖尾
- 组件卸载时清理 WebGL 资源

---

## 其他关键决策

### 图表方案：纯 SVG，零图表库

设计明确要求所有图表使用纯 SVG 绘制。所有图表组件（LineChart、BarChart、DonutChart 等）手动计算 SVG path、坐标点和布局，不引入 recharts/d3-chart 等库。坐标轴、网格线、数据点均为原生 SVG 元素。

### 字体加载策略

Noto Sans TC 和 Inter 通过 Google Fonts CDN 引入（`<link>` 标签）。等宽字体 Courier New 为系统字体，无需额外加载。

### 响应式断点

- Desktop > 1024px：完整多栏布局
- Tablet 768-1024px：双栏改为单栏堆叠，KPI 改为 2x2 网格
- Mobile < 768px：全部单栏，图表缩放，表格水平滚动

通过 CSS Grid + Flexbox + Tailwind 响应式类实现，无额外库。
