/// <reference types="../../../../../../home/kimi/.npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/template-helpers.d.ts" />
/// <reference types="../../../../../../home/kimi/.npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/props-fallback.d.ts" />
import { ref, computed, h } from 'vue';
import NavigationHeader from '@/components/NavigationHeader.vue';
import Footer from '@/components/Footer.vue';
import ScrollReveal from '@/components/ScrollReveal.vue';
import LineChart from '@/components/LineChart.vue';
import DonutChart from '@/components/DonutChart.vue';
import DataTable from '@/components/DataTable.vue';
import ScatterPlot from '@/components/ScatterPlot.vue';
import BarChart from '@/components/BarChart.vue';
import ParticleCanvas from '@/components/ParticleCanvas.vue';
import { kpiData, portfolioValueData, allocationData, performanceAttribution, riskScatterData, drawdownData } from '@/data/portfolioData';
const timeRange = ref('1Y');
const timeRanges = ['1Y', '6M', '3M', '1M', 'YTD'];
const hoveredSegment = ref(null);
const drawdownAreaPoints = computed(() => {
    const pts = drawdownData.values.map((v, i) => `${60 + (i / 11) * 420},${20 + (1 - v / 15) * 200}`);
    return `${60},${20 + (1 - (-8.2) / 15) * 200} ${pts.join(' ')} 480,${20 + 200}`;
});
const drawdownLinePoints = computed(() => drawdownData.values.map((v, i) => `${60 + (i / 11) * 420},${20 + (1 - v / 15) * 200}`).join(' '));
const perfCards = [
    {
        title: '產業別貢獻',
        component: BarChart,
        props: { data: performanceAttribution.sector.map(s => ({ label: s.label, value: s.value, color: '#000000' })), width: 280 }
    },
    {
        title: '選股 Alpha',
        component: { setup() {
                return () => h('div', { class: 'flex flex-col' }, performanceAttribution.alpha.map((a, i) => h('div', {
                    class: 'flex justify-between items-center py-2',
                    style: { borderBottom: i < performanceAttribution.alpha.length - 1 ? '1px solid #E0E0E0' : 'none' }
                }, [h('span', { class: 'text-sm', style: { color: '#000000' } }, a.name), h('span', {
                        class: 'text-sm font-medium', style: { color: a.value > 0 ? '#000000' : '#666666' }
                    }, `${a.value > 0 ? '+' : ''}${a.value}%`)])));
            } }
    },
    {
        title: '時間加權報酬',
        component: { setup() {
                return () => h('svg', { width: '100%', height: 120, viewBox: '0 0 280 120' }, [
                    h('line', { x1: 10, y1: 60, x2: 270, y2: 60, stroke: '#E0E0E0', strokeWidth: 1 }),
                    ...performanceAttribution.monthlyReturns.flatMap((v, i) => {
                        const x = (i / 11) * 260 + 10, y = 60 - v * 8;
                        const els = [];
                        if (i > 0) {
                            const px = ((i - 1) / 11) * 260 + 10, py = 60 - performanceAttribution.monthlyReturns[i - 1] * 8;
                            els.push(h('line', { x1: px, y1: py, x2: x, y2: y, stroke: v >= 0 ? '#000000' : '#999999', strokeWidth: 1.5 }));
                        }
                        els.push(h('circle', { cx: x, cy: y, r: 3, fill: v >= 0 ? '#000000' : '#999999' }));
                        return els;
                    })
                ]);
            } }
    },
    {
        title: '風險指標',
        component: { setup() {
                return () => h('div', { class: 'flex flex-col gap-4' }, [
                    { label: '波動率', value: performanceAttribution.risk.volatility },
                    { label: '最大回撤', value: performanceAttribution.risk.maxDrawdown },
                    { label: '索提諾比率', value: performanceAttribution.risk.sortino },
                    { label: '資訊比率', value: performanceAttribution.risk.infoRatio },
                ].map(r => h('div', { class: 'flex justify-between items-center' }, [
                    h('span', { class: 'text-caption uppercase', style: { color: '#666666' } }, r.label),
                    h('span', { class: 'text-data', style: { color: '#000000', fontSize: '22px' } }, r.value)
                ])));
            } }
    },
];
const __VLS_ctx = {
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "min-h-screen" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['min-h-screen']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "relative" },
});
/** @type {__VLS_StyleScopedClasses['relative']} */ ;
const __VLS_0 = ParticleCanvas;
// @ts-ignore
const __VLS_1 = __VLS_asFunctionalComponent1(__VLS_0, new __VLS_0({
    theme: "warm",
}));
const __VLS_2 = __VLS_1({
    theme: "warm",
}, ...__VLS_functionalComponentArgsRest(__VLS_1));
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "relative" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['relative']} */ ;
const __VLS_5 = NavigationHeader;
// @ts-ignore
const __VLS_6 = __VLS_asFunctionalComponent1(__VLS_5, new __VLS_5({
    isDark: (false),
}));
const __VLS_7 = __VLS_6({
    isDark: (false),
}, ...__VLS_functionalComponentArgsRest(__VLS_6));
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "page-padding relative" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['page-padding']} */ ;
/** @type {__VLS_StyleScopedClasses['relative']} */ ;
const __VLS_10 = ScrollReveal || ScrollReveal;
// @ts-ignore
const __VLS_11 = __VLS_asFunctionalComponent1(__VLS_10, new __VLS_10({}));
const __VLS_12 = __VLS_11({}, ...__VLS_functionalComponentArgsRest(__VLS_11));
const { default: __VLS_15 } = __VLS_13.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "w-full border-t" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['w-full']} */ ;
/** @type {__VLS_StyleScopedClasses['border-t']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "grid grid-cols-2 lg:grid-cols-4" },
});
/** @type {__VLS_StyleScopedClasses['grid']} */ ;
/** @type {__VLS_StyleScopedClasses['grid-cols-2']} */ ;
/** @type {__VLS_StyleScopedClasses['lg:grid-cols-4']} */ ;
for (const [kpi, i] of __VLS_vFor((__VLS_ctx.kpiData))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        key: (i),
        ...{ class: "flex flex-col justify-center items-center py-6 transition-all duration-300 hover:scale-[1.02] group relative cursor-default" },
        ...{ style: ({ borderLeft: i === 0 ? 'none' : '1px solid #E0E0E0', borderRight: '1px solid #E0E0E0', borderBottom: '1px solid #E0E0E0', height: '120px' }) },
    });
    /** @type {__VLS_StyleScopedClasses['flex']} */ ;
    /** @type {__VLS_StyleScopedClasses['flex-col']} */ ;
    /** @type {__VLS_StyleScopedClasses['justify-center']} */ ;
    /** @type {__VLS_StyleScopedClasses['items-center']} */ ;
    /** @type {__VLS_StyleScopedClasses['py-6']} */ ;
    /** @type {__VLS_StyleScopedClasses['transition-all']} */ ;
    /** @type {__VLS_StyleScopedClasses['duration-300']} */ ;
    /** @type {__VLS_StyleScopedClasses['hover:scale-[1.02]']} */ ;
    /** @type {__VLS_StyleScopedClasses['group']} */ ;
    /** @type {__VLS_StyleScopedClasses['relative']} */ ;
    /** @type {__VLS_StyleScopedClasses['cursor-default']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "text-caption uppercase mb-2" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['text-caption']} */ ;
    /** @type {__VLS_StyleScopedClasses['uppercase']} */ ;
    /** @type {__VLS_StyleScopedClasses['mb-2']} */ ;
    (kpi.label);
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "text-data" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['text-data']} */ ;
    (kpi.value);
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "text-xs mt-1" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['text-xs']} */ ;
    /** @type {__VLS_StyleScopedClasses['mt-1']} */ ;
    (kpi.sub);
    __VLS_asFunctionalElement1(__VLS_intrinsics.div)({
        ...{ class: "absolute left-0 top-0 bottom-0 w-0 group-hover:w-0.5 transition-all duration-300" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['absolute']} */ ;
    /** @type {__VLS_StyleScopedClasses['left-0']} */ ;
    /** @type {__VLS_StyleScopedClasses['top-0']} */ ;
    /** @type {__VLS_StyleScopedClasses['bottom-0']} */ ;
    /** @type {__VLS_StyleScopedClasses['w-0']} */ ;
    /** @type {__VLS_StyleScopedClasses['group-hover:w-0.5']} */ ;
    /** @type {__VLS_StyleScopedClasses['transition-all']} */ ;
    /** @type {__VLS_StyleScopedClasses['duration-300']} */ ;
    // @ts-ignore
    [kpiData,];
}
// @ts-ignore
[];
var __VLS_13;
const __VLS_16 = ScrollReveal || ScrollReveal;
// @ts-ignore
const __VLS_17 = __VLS_asFunctionalComponent1(__VLS_16, new __VLS_16({
    ...{ class: "mt-20" },
}));
const __VLS_18 = __VLS_17({
    ...{ class: "mt-20" },
}, ...__VLS_functionalComponentArgsRest(__VLS_17));
/** @type {__VLS_StyleScopedClasses['mt-20']} */ ;
const { default: __VLS_21 } = __VLS_19.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "w-full border" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['w-full']} */ ;
/** @type {__VLS_StyleScopedClasses['border']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "flex flex-col sm:flex-row sm:items-center sm:justify-between p-5 border-b" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['flex']} */ ;
/** @type {__VLS_StyleScopedClasses['flex-col']} */ ;
/** @type {__VLS_StyleScopedClasses['sm:flex-row']} */ ;
/** @type {__VLS_StyleScopedClasses['sm:items-center']} */ ;
/** @type {__VLS_StyleScopedClasses['sm:justify-between']} */ ;
/** @type {__VLS_StyleScopedClasses['p-5']} */ ;
/** @type {__VLS_StyleScopedClasses['border-b']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.h2, __VLS_intrinsics.h2)({
    ...{ class: "font-heading text-2xl font-semibold" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['font-heading']} */ ;
/** @type {__VLS_StyleScopedClasses['text-2xl']} */ ;
/** @type {__VLS_StyleScopedClasses['font-semibold']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-caption block mt-1" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-caption']} */ ;
/** @type {__VLS_StyleScopedClasses['block']} */ ;
/** @type {__VLS_StyleScopedClasses['mt-1']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-caption block mt-0.5" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-caption']} */ ;
/** @type {__VLS_StyleScopedClasses['block']} */ ;
/** @type {__VLS_StyleScopedClasses['mt-0.5']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "flex gap-2 mt-4 sm:mt-0" },
});
/** @type {__VLS_StyleScopedClasses['flex']} */ ;
/** @type {__VLS_StyleScopedClasses['gap-2']} */ ;
/** @type {__VLS_StyleScopedClasses['mt-4']} */ ;
/** @type {__VLS_StyleScopedClasses['sm:mt-0']} */ ;
for (const [r] of __VLS_vFor((__VLS_ctx.timeRanges))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (...[$event]) => {
                __VLS_ctx.timeRange = r;
                // @ts-ignore
                [timeRanges, timeRange,];
            } },
        key: (r),
        ...{ class: "font-mono text-xs px-3 py-1 transition-all duration-200" },
        ...{ style: ({ border: `1px solid ${__VLS_ctx.timeRange === r ? '#000000' : '#E0E0E0'}`, backgroundColor: __VLS_ctx.timeRange === r ? '#000000' : 'transparent', color: __VLS_ctx.timeRange === r ? '#FFFFFF' : '#666666' }) },
    });
    /** @type {__VLS_StyleScopedClasses['font-mono']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-xs']} */ ;
    /** @type {__VLS_StyleScopedClasses['px-3']} */ ;
    /** @type {__VLS_StyleScopedClasses['py-1']} */ ;
    /** @type {__VLS_StyleScopedClasses['transition-all']} */ ;
    /** @type {__VLS_StyleScopedClasses['duration-200']} */ ;
    (r);
    // @ts-ignore
    [timeRange, timeRange, timeRange,];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "p-5" },
});
/** @type {__VLS_StyleScopedClasses['p-5']} */ ;
const __VLS_22 = LineChart;
// @ts-ignore
const __VLS_23 = __VLS_asFunctionalComponent1(__VLS_22, new __VLS_22({
    data: (__VLS_ctx.portfolioValueData.values),
    labels: (__VLS_ctx.portfolioValueData.labels),
    yAxisLabels: (__VLS_ctx.portfolioValueData.yAxisLabels),
    height: (400),
    lineColor: "#000000",
    showArea: (true),
}));
const __VLS_24 = __VLS_23({
    data: (__VLS_ctx.portfolioValueData.values),
    labels: (__VLS_ctx.portfolioValueData.labels),
    yAxisLabels: (__VLS_ctx.portfolioValueData.yAxisLabels),
    height: (400),
    lineColor: "#000000",
    showArea: (true),
}, ...__VLS_functionalComponentArgsRest(__VLS_23));
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "grid grid-cols-1 sm:grid-cols-3 gap-4 p-5 border-t" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['grid']} */ ;
/** @type {__VLS_StyleScopedClasses['grid-cols-1']} */ ;
/** @type {__VLS_StyleScopedClasses['sm:grid-cols-3']} */ ;
/** @type {__VLS_StyleScopedClasses['gap-4']} */ ;
/** @type {__VLS_StyleScopedClasses['p-5']} */ ;
/** @type {__VLS_StyleScopedClasses['border-t']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-sm" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-sm']} */ ;
(__VLS_ctx.portfolioValueData.summary.start);
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-sm" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-sm']} */ ;
(__VLS_ctx.portfolioValueData.summary.high);
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-sm" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-sm']} */ ;
(__VLS_ctx.portfolioValueData.summary.low);
// @ts-ignore
[portfolioValueData, portfolioValueData, portfolioValueData, portfolioValueData, portfolioValueData, portfolioValueData,];
var __VLS_19;
const __VLS_27 = ScrollReveal || ScrollReveal;
// @ts-ignore
const __VLS_28 = __VLS_asFunctionalComponent1(__VLS_27, new __VLS_27({
    ...{ class: "mt-20" },
}));
const __VLS_29 = __VLS_28({
    ...{ class: "mt-20" },
}, ...__VLS_functionalComponentArgsRest(__VLS_28));
/** @type {__VLS_StyleScopedClasses['mt-20']} */ ;
const { default: __VLS_32 } = __VLS_30.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "w-full border grid grid-cols-1 lg:grid-cols-5" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['w-full']} */ ;
/** @type {__VLS_StyleScopedClasses['border']} */ ;
/** @type {__VLS_StyleScopedClasses['grid']} */ ;
/** @type {__VLS_StyleScopedClasses['grid-cols-1']} */ ;
/** @type {__VLS_StyleScopedClasses['lg:grid-cols-5']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "lg:col-span-2 flex flex-col items-center justify-center py-10" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['lg:col-span-2']} */ ;
/** @type {__VLS_StyleScopedClasses['flex']} */ ;
/** @type {__VLS_StyleScopedClasses['flex-col']} */ ;
/** @type {__VLS_StyleScopedClasses['items-center']} */ ;
/** @type {__VLS_StyleScopedClasses['justify-center']} */ ;
/** @type {__VLS_StyleScopedClasses['py-10']} */ ;
const __VLS_33 = DonutChart;
// @ts-ignore
const __VLS_34 = __VLS_asFunctionalComponent1(__VLS_33, new __VLS_33({
    ...{ 'onSegmentHover': {} },
    segments: (__VLS_ctx.allocationData.segments),
    centerLabel: "NT$12.58M",
    centerSubLabel: "4 類資產",
    activeIndex: (__VLS_ctx.hoveredSegment),
}));
const __VLS_35 = __VLS_34({
    ...{ 'onSegmentHover': {} },
    segments: (__VLS_ctx.allocationData.segments),
    centerLabel: "NT$12.58M",
    centerSubLabel: "4 類資產",
    activeIndex: (__VLS_ctx.hoveredSegment),
}, ...__VLS_functionalComponentArgsRest(__VLS_34));
let __VLS_38;
const __VLS_39 = {
    ...{ segmentHover: {} },
    onSegmentHover: (...[$event]) => {
        __VLS_ctx.hoveredSegment = $event;
        // @ts-ignore
        [allocationData, hoveredSegment, hoveredSegment,];
    },
};
var __VLS_36;
var __VLS_37;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "lg:col-span-3 p-5" },
});
/** @type {__VLS_StyleScopedClasses['lg:col-span-3']} */ ;
/** @type {__VLS_StyleScopedClasses['p-5']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "mb-4" },
});
/** @type {__VLS_StyleScopedClasses['mb-4']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.h2, __VLS_intrinsics.h2)({
    ...{ class: "font-heading text-2xl font-semibold" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['font-heading']} */ ;
/** @type {__VLS_StyleScopedClasses['text-2xl']} */ ;
/** @type {__VLS_StyleScopedClasses['font-semibold']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-caption" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-caption']} */ ;
const __VLS_40 = DataTable;
// @ts-ignore
const __VLS_41 = __VLS_asFunctionalComponent1(__VLS_40, new __VLS_40({
    headers: (['代碼', '名稱', '類別', '持有股數', '現價', '市值', '占比', '損益']),
    rows: (__VLS_ctx.allocationData.holdings.map(h => [h.code, h.name, h.category, h.shares, h.price, h.value, h.ratio, h.pnl])),
    highlightRow: (__VLS_ctx.hoveredSegment),
    onRowHover: ((idx) => __VLS_ctx.hoveredSegment = idx),
}));
const __VLS_42 = __VLS_41({
    headers: (['代碼', '名稱', '類別', '持有股數', '現價', '市值', '占比', '損益']),
    rows: (__VLS_ctx.allocationData.holdings.map(h => [h.code, h.name, h.category, h.shares, h.price, h.value, h.ratio, h.pnl])),
    highlightRow: (__VLS_ctx.hoveredSegment),
    onRowHover: ((idx) => __VLS_ctx.hoveredSegment = idx),
}, ...__VLS_functionalComponentArgsRest(__VLS_41));
// @ts-ignore
[allocationData, hoveredSegment, hoveredSegment,];
var __VLS_30;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "mt-20" },
});
/** @type {__VLS_StyleScopedClasses['mt-20']} */ ;
const __VLS_45 = ScrollReveal || ScrollReveal;
// @ts-ignore
const __VLS_46 = __VLS_asFunctionalComponent1(__VLS_45, new __VLS_45({}));
const __VLS_47 = __VLS_46({}, ...__VLS_functionalComponentArgsRest(__VLS_46));
const { default: __VLS_50 } = __VLS_48.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "mb-6" },
});
/** @type {__VLS_StyleScopedClasses['mb-6']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.h2, __VLS_intrinsics.h2)({
    ...{ class: "font-heading text-2xl font-semibold" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['font-heading']} */ ;
/** @type {__VLS_StyleScopedClasses['text-2xl']} */ ;
/** @type {__VLS_StyleScopedClasses['font-semibold']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-caption" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-caption']} */ ;
// @ts-ignore
[];
var __VLS_48;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-3" },
});
/** @type {__VLS_StyleScopedClasses['grid']} */ ;
/** @type {__VLS_StyleScopedClasses['grid-cols-1']} */ ;
/** @type {__VLS_StyleScopedClasses['md:grid-cols-2']} */ ;
/** @type {__VLS_StyleScopedClasses['lg:grid-cols-4']} */ ;
/** @type {__VLS_StyleScopedClasses['gap-3']} */ ;
for (const [card, i] of __VLS_vFor((__VLS_ctx.perfCards))) {
    const __VLS_51 = ScrollReveal || ScrollReveal;
    // @ts-ignore
    const __VLS_52 = __VLS_asFunctionalComponent1(__VLS_51, new __VLS_51({
        key: (i),
        delay: (i * 0.15),
    }));
    const __VLS_53 = __VLS_52({
        key: (i),
        delay: (i * 0.15),
    }, ...__VLS_functionalComponentArgsRest(__VLS_52));
    const { default: __VLS_56 } = __VLS_54.slots;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "border p-5 h-full" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['border']} */ ;
    /** @type {__VLS_StyleScopedClasses['p-5']} */ ;
    /** @type {__VLS_StyleScopedClasses['h-full']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.h3, __VLS_intrinsics.h3)({
        ...{ class: "font-heading text-lg font-medium mb-4" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['font-heading']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-lg']} */ ;
    /** @type {__VLS_StyleScopedClasses['font-medium']} */ ;
    /** @type {__VLS_StyleScopedClasses['mb-4']} */ ;
    (card.title);
    const __VLS_57 = (card.component);
    // @ts-ignore
    const __VLS_58 = __VLS_asFunctionalComponent1(__VLS_57, new __VLS_57({
        ...(card.props),
    }));
    const __VLS_59 = __VLS_58({
        ...(card.props),
    }, ...__VLS_functionalComponentArgsRest(__VLS_58));
    // @ts-ignore
    [perfCards,];
    var __VLS_54;
    // @ts-ignore
    [];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "mt-20 border grid grid-cols-1 lg:grid-cols-2 gap-0" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['mt-20']} */ ;
/** @type {__VLS_StyleScopedClasses['border']} */ ;
/** @type {__VLS_StyleScopedClasses['grid']} */ ;
/** @type {__VLS_StyleScopedClasses['grid-cols-1']} */ ;
/** @type {__VLS_StyleScopedClasses['lg:grid-cols-2']} */ ;
/** @type {__VLS_StyleScopedClasses['gap-0']} */ ;
const __VLS_62 = ScrollReveal || ScrollReveal;
// @ts-ignore
const __VLS_63 = __VLS_asFunctionalComponent1(__VLS_62, new __VLS_62({
    ...{ class: "p-5" },
    ...{ style: {} },
}));
const __VLS_64 = __VLS_63({
    ...{ class: "p-5" },
    ...{ style: {} },
}, ...__VLS_functionalComponentArgsRest(__VLS_63));
/** @type {__VLS_StyleScopedClasses['p-5']} */ ;
const { default: __VLS_67 } = __VLS_65.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.h2, __VLS_intrinsics.h2)({
    ...{ class: "font-heading text-2xl font-semibold mb-4" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['font-heading']} */ ;
/** @type {__VLS_StyleScopedClasses['text-2xl']} */ ;
/** @type {__VLS_StyleScopedClasses['font-semibold']} */ ;
/** @type {__VLS_StyleScopedClasses['mb-4']} */ ;
const __VLS_68 = ScatterPlot;
// @ts-ignore
const __VLS_69 = __VLS_asFunctionalComponent1(__VLS_68, new __VLS_68({
    data: (__VLS_ctx.riskScatterData),
    xAxisLabel: "波動率（標準差）",
    yAxisLabel: "預期報酬率",
    xRange: ([0, 30]),
    yRange: ([-5, 25]),
    frontierCurve: ([[5, 2], [8, 5], [10, 7], [12, 9], [15, 11], [18, 13], [22, 15], [25, 16]]),
}));
const __VLS_70 = __VLS_69({
    data: (__VLS_ctx.riskScatterData),
    xAxisLabel: "波動率（標準差）",
    yAxisLabel: "預期報酬率",
    xRange: ([0, 30]),
    yRange: ([-5, 25]),
    frontierCurve: ([[5, 2], [8, 5], [10, 7], [12, 9], [15, 11], [18, 13], [22, 15], [25, 16]]),
}, ...__VLS_functionalComponentArgsRest(__VLS_69));
// @ts-ignore
[riskScatterData,];
var __VLS_65;
const __VLS_73 = ScrollReveal || ScrollReveal;
// @ts-ignore
const __VLS_74 = __VLS_asFunctionalComponent1(__VLS_73, new __VLS_73({
    ...{ class: "p-5" },
}));
const __VLS_75 = __VLS_74({
    ...{ class: "p-5" },
}, ...__VLS_functionalComponentArgsRest(__VLS_74));
/** @type {__VLS_StyleScopedClasses['p-5']} */ ;
const { default: __VLS_78 } = __VLS_76.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.h2, __VLS_intrinsics.h2)({
    ...{ class: "font-heading text-2xl font-semibold mb-4" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['font-heading']} */ ;
/** @type {__VLS_StyleScopedClasses['text-2xl']} */ ;
/** @type {__VLS_StyleScopedClasses['font-semibold']} */ ;
/** @type {__VLS_StyleScopedClasses['mb-4']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
    width: "100%",
    height: "250",
    viewBox: "0 0 500 250",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.polygon)({
    points: (__VLS_ctx.drawdownAreaPoints),
    fill: "rgba(0,0,0,0.06)",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.polyline)({
    points: (__VLS_ctx.drawdownLinePoints),
    fill: "none",
    stroke: "#000000",
    'stroke-width': "1.5",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.line)({
    x1: (60 + (2 / 11) * 420),
    y1: (20 + (1 - (-8.2) / 15) * 200),
    x2: (60 + (2 / 11) * 420),
    y2: (220),
    stroke: "#000000",
    'stroke-width': "1",
    'stroke-dasharray': "4 4",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
    x: (60 + (2 / 11) * 420 + 5),
    y: (20 + (1 - (-8.2) / 15) * 200 - 5),
    fill: "#000000",
    'font-size': "11",
    'font-weight': "600",
});
for (const [v] of __VLS_vFor(([0, -5, -10, -15]))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
        key: (v),
        x: "55",
        y: (20 + (1 - v / 15) * 200 + 4),
        'text-anchor': "end",
        fill: "#666666",
        'font-size': "10",
    });
    (v);
    // @ts-ignore
    [drawdownAreaPoints, drawdownLinePoints,];
}
for (const [l, i] of __VLS_vFor((__VLS_ctx.drawdownData.labels))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
        key: (l),
        x: (60 + (i / 11) * 420),
        y: "240",
        'text-anchor': "middle",
        fill: "#666666",
        'font-size': "9",
    });
    (l);
    // @ts-ignore
    [drawdownData,];
}
// @ts-ignore
[];
var __VLS_76;
__VLS_asFunctionalElement1(__VLS_intrinsics.div)({
    ...{ class: "h-20" },
});
/** @type {__VLS_StyleScopedClasses['h-20']} */ ;
const __VLS_79 = Footer;
// @ts-ignore
const __VLS_80 = __VLS_asFunctionalComponent1(__VLS_79, new __VLS_79({}));
const __VLS_81 = __VLS_80({}, ...__VLS_functionalComponentArgsRest(__VLS_80));
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({});
export default {};
