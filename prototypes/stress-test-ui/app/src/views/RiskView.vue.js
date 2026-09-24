import { ref, computed } from 'vue';
import NavigationHeader from '@/components/NavigationHeader.vue';
import Footer from '@/components/Footer.vue';
import ScrollReveal from '@/components/ScrollReveal.vue';
import ParticleCanvas from '@/components/ParticleCanvas.vue';
import { riskKPIData, varResults, varHistogram, esResults, varEsComparison, stressScenarios, backtestData, monteCarloPaths, monteCarloPercentiles } from '@/data/riskData';
const selectedScenario = ref(null);
function severityColor(s) {
    const map = { low: '#666666', medium: '#FF6B00', high: '#8B1A2B', extreme: '#8B1A2B' };
    return map[s];
}
function severityLabel(s) {
    const map = { low: '低', medium: '中', high: '高', extreme: '極高' };
    return map[s];
}
const displayedPaths = monteCarloPaths.slice(0, 8);
const mcP1 = computed(() => {
    const upper = monteCarloPercentiles.p99.map((v, i) => `${70 + (i / 252) * 780},${200 - v * 400}`).join(' ');
    const lower = monteCarloPercentiles.p1.map((v, i) => `${70 + ((252 - i) / 252) * 780},${200 - v * 400}`).join(' ');
    return upper + ' ' + lower;
});
const mcP5 = computed(() => {
    const upper = monteCarloPercentiles.p95.map((v, i) => `${70 + (i / 252) * 780},${200 - v * 400}`).join(' ');
    const lower = monteCarloPercentiles.p5.map((v, i) => `${70 + ((252 - i) / 252) * 780},${200 - v * 400}`).join(' ');
    return upper + ' ' + lower;
});
const mcStats = [
    { label: '一年勝率', value: '62.5%', color: '#FFFFFF' },
    { label: '期望報酬', value: '+8.2%', color: '#FFFFFF' },
    { label: '5% 最壞情境', value: '-28.5%', color: '#FF6B00' },
    { label: '1% 最壞情境', value: '-38.2%', color: '#8B1A2B' },
    { label: '路徑模擬次數', value: '10,000', color: '#666666' },
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
    theme: "red",
}));
const __VLS_2 = __VLS_1({
    theme: "red",
}, ...__VLS_functionalComponentArgsRest(__VLS_1));
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "relative" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['relative']} */ ;
const __VLS_5 = NavigationHeader;
// @ts-ignore
const __VLS_6 = __VLS_asFunctionalComponent1(__VLS_5, new __VLS_5({
    isDark: (true),
}));
const __VLS_7 = __VLS_6({
    isDark: (true),
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
    ...{ class: "grid grid-cols-2 lg:grid-cols-5" },
});
/** @type {__VLS_StyleScopedClasses['grid']} */ ;
/** @type {__VLS_StyleScopedClasses['grid-cols-2']} */ ;
/** @type {__VLS_StyleScopedClasses['lg:grid-cols-5']} */ ;
for (const [kpi, i] of __VLS_vFor((__VLS_ctx.riskKPIData))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        key: (i),
        ...{ class: "flex flex-col justify-center items-center py-6 group relative cursor-default" },
        ...{ style: ({ borderLeft: i === 0 ? 'none' : '1px solid #333333', borderRight: '1px solid #333333', borderBottom: '1px solid #333333', height: '130px', backgroundColor: '#0A0A0A' }) },
    });
    /** @type {__VLS_StyleScopedClasses['flex']} */ ;
    /** @type {__VLS_StyleScopedClasses['flex-col']} */ ;
    /** @type {__VLS_StyleScopedClasses['justify-center']} */ ;
    /** @type {__VLS_StyleScopedClasses['items-center']} */ ;
    /** @type {__VLS_StyleScopedClasses['py-6']} */ ;
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
        ...{ style: ({ color: kpi.color }) },
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
    [riskKPIData,];
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
    ...{ class: "p-5 border-b" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['p-5']} */ ;
/** @type {__VLS_StyleScopedClasses['border-b']} */ ;
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
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "p-5" },
});
/** @type {__VLS_StyleScopedClasses['p-5']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "grid grid-cols-1 lg:grid-cols-2 gap-8" },
});
/** @type {__VLS_StyleScopedClasses['grid']} */ ;
/** @type {__VLS_StyleScopedClasses['grid-cols-1']} */ ;
/** @type {__VLS_StyleScopedClasses['lg:grid-cols-2']} */ ;
/** @type {__VLS_StyleScopedClasses['gap-8']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.table, __VLS_intrinsics.table)({
    ...{ class: "w-full" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['w-full']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.thead, __VLS_intrinsics.thead)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.tr, __VLS_intrinsics.tr)({
    ...{ style: {} },
});
__VLS_asFunctionalElement1(__VLS_intrinsics.th, __VLS_intrinsics.th)({
    ...{ class: "text-caption uppercase text-left px-4" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-caption']} */ ;
/** @type {__VLS_StyleScopedClasses['uppercase']} */ ;
/** @type {__VLS_StyleScopedClasses['text-left']} */ ;
/** @type {__VLS_StyleScopedClasses['px-4']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.th, __VLS_intrinsics.th)({
    ...{ class: "text-caption uppercase text-right px-4" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-caption']} */ ;
/** @type {__VLS_StyleScopedClasses['uppercase']} */ ;
/** @type {__VLS_StyleScopedClasses['text-right']} */ ;
/** @type {__VLS_StyleScopedClasses['px-4']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.th, __VLS_intrinsics.th)({
    ...{ class: "text-caption uppercase text-right px-4" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-caption']} */ ;
/** @type {__VLS_StyleScopedClasses['uppercase']} */ ;
/** @type {__VLS_StyleScopedClasses['text-right']} */ ;
/** @type {__VLS_StyleScopedClasses['px-4']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.tbody, __VLS_intrinsics.tbody)({});
for (const [v, i] of __VLS_vFor((__VLS_ctx.varResults))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.tr, __VLS_intrinsics.tr)({
        key: (i),
        ...{ style: ({ backgroundColor: i % 2 === 0 ? '#0A0A0A' : '#111111', height: 48 }) },
    });
    __VLS_asFunctionalElement1(__VLS_intrinsics.td, __VLS_intrinsics.td)({
        ...{ class: "px-4 text-sm" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['px-4']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-sm']} */ ;
    (v.method);
    __VLS_asFunctionalElement1(__VLS_intrinsics.td, __VLS_intrinsics.td)({
        ...{ class: "px-4 text-sm text-right font-mono" },
        ...{ style: ({ color: v.var95 < -4.8 ? '#FF6B00' : '#FFFFFF', borderBottom: '1px solid #333333' }) },
    });
    /** @type {__VLS_StyleScopedClasses['px-4']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-sm']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-right']} */ ;
    /** @type {__VLS_StyleScopedClasses['font-mono']} */ ;
    (v.var95);
    __VLS_asFunctionalElement1(__VLS_intrinsics.td, __VLS_intrinsics.td)({
        ...{ class: "px-4 text-sm text-right font-mono" },
        ...{ style: ({ color: '#8B1A2B', borderBottom: '1px solid #333333' }) },
    });
    /** @type {__VLS_StyleScopedClasses['px-4']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-sm']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-right']} */ ;
    /** @type {__VLS_StyleScopedClasses['font-mono']} */ ;
    (v.var99);
    // @ts-ignore
    [varResults,];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
    ...{ class: "text-xs mt-3" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-xs']} */ ;
/** @type {__VLS_StyleScopedClasses['mt-3']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.h3, __VLS_intrinsics.h3)({
    ...{ class: "text-sm font-medium mb-3" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-sm']} */ ;
/** @type {__VLS_StyleScopedClasses['font-medium']} */ ;
/** @type {__VLS_StyleScopedClasses['mb-3']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
    width: "100%",
    height: "240",
    viewBox: "0 0 400 240",
    ...{ class: "w-full" },
});
/** @type {__VLS_StyleScopedClasses['w-full']} */ ;
for (const [i] of __VLS_vFor((5))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.line)({
        key: (`g-${i}),
'`), ': true,: x1, "50": ,
        y1: (30 + (i - 1) * 40),
        x2: "380",
        y2: (30 + (i - 1) * 40),
        stroke: "#333333",
        'stroke-width': "1",
        'stroke-dasharray': "4 4",
    });
    // @ts-ignore
    [];
}
for (const [i] of __VLS_vFor((5))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
        key: (`gy-${i}),
'`), ': true,: x, "45": ,
        y: (30 + (i - 1) * 40 + 4),
        'text-anchor': "end",
        fill: "#666666",
        'font-size': "10",
    });
    (Math.round(50 - (i - 1) * 12));
    // @ts-ignore
    [];
}
for (const [bin, i] of __VLS_vFor((__VLS_ctx.varHistogram))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.g, __VLS_intrinsics.g)({
        key: (i),
    });
    __VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
        x: (55 + i * 29),
        y: (210 - (bin.count / 50) * 180),
        width: "24",
        height: ((bin.count / 50) * 180),
        fill: (bin.isTail99 ? '#8B1A2B' : bin.isTail95 ? '#FF6B00' : '#333333'),
        ...{ class: "transition-all" },
    });
    /** @type {__VLS_StyleScopedClasses['transition-all']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
        x: (55 + i * 29 + 12),
        y: "228",
        'text-anchor': "middle",
        fill: "#666666",
        'font-size': "8",
    });
    (bin.bin);
    // @ts-ignore
    [varHistogram,];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.g, __VLS_intrinsics.g)({
    transform: "translate(55, 12)",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
    x: "0",
    y: "-6",
    width: "10",
    height: "8",
    fill: "#333333",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
    x: "14",
    y: "0",
    fill: "#666666",
    'font-size': "9",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
    x: "70",
    y: "-6",
    width: "10",
    height: "8",
    fill: "#FF6B00",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
    x: "84",
    y: "0",
    fill: "#666666",
    'font-size': "9",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
    x: "145",
    y: "-6",
    width: "10",
    height: "8",
    fill: "#8B1A2B",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
    x: "159",
    y: "0",
    fill: "#666666",
    'font-size': "9",
});
// @ts-ignore
[];
var __VLS_19;
const __VLS_22 = ScrollReveal || ScrollReveal;
// @ts-ignore
const __VLS_23 = __VLS_asFunctionalComponent1(__VLS_22, new __VLS_22({
    ...{ class: "mt-20" },
}));
const __VLS_24 = __VLS_23({
    ...{ class: "mt-20" },
}, ...__VLS_functionalComponentArgsRest(__VLS_23));
/** @type {__VLS_StyleScopedClasses['mt-20']} */ ;
const { default: __VLS_27 } = __VLS_25.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "w-full border" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['w-full']} */ ;
/** @type {__VLS_StyleScopedClasses['border']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "p-5 border-b" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['p-5']} */ ;
/** @type {__VLS_StyleScopedClasses['border-b']} */ ;
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
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "p-5" },
});
/** @type {__VLS_StyleScopedClasses['p-5']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "grid grid-cols-1 lg:grid-cols-2 gap-8" },
});
/** @type {__VLS_StyleScopedClasses['grid']} */ ;
/** @type {__VLS_StyleScopedClasses['grid-cols-1']} */ ;
/** @type {__VLS_StyleScopedClasses['lg:grid-cols-2']} */ ;
/** @type {__VLS_StyleScopedClasses['gap-8']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.table, __VLS_intrinsics.table)({
    ...{ class: "w-full" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['w-full']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.thead, __VLS_intrinsics.thead)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.tr, __VLS_intrinsics.tr)({
    ...{ style: {} },
});
__VLS_asFunctionalElement1(__VLS_intrinsics.th, __VLS_intrinsics.th)({
    ...{ class: "text-caption uppercase text-left px-4" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-caption']} */ ;
/** @type {__VLS_StyleScopedClasses['uppercase']} */ ;
/** @type {__VLS_StyleScopedClasses['text-left']} */ ;
/** @type {__VLS_StyleScopedClasses['px-4']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.th, __VLS_intrinsics.th)({
    ...{ class: "text-caption uppercase text-right px-4" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-caption']} */ ;
/** @type {__VLS_StyleScopedClasses['uppercase']} */ ;
/** @type {__VLS_StyleScopedClasses['text-right']} */ ;
/** @type {__VLS_StyleScopedClasses['px-4']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.th, __VLS_intrinsics.th)({
    ...{ class: "text-caption uppercase text-right px-4" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-caption']} */ ;
/** @type {__VLS_StyleScopedClasses['uppercase']} */ ;
/** @type {__VLS_StyleScopedClasses['text-right']} */ ;
/** @type {__VLS_StyleScopedClasses['px-4']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.tbody, __VLS_intrinsics.tbody)({});
for (const [e, i] of __VLS_vFor((__VLS_ctx.esResults))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.tr, __VLS_intrinsics.tr)({
        key: (i),
        ...{ style: ({ backgroundColor: i % 2 === 0 ? '#0A0A0A' : '#111111', height: 48 }) },
    });
    __VLS_asFunctionalElement1(__VLS_intrinsics.td, __VLS_intrinsics.td)({
        ...{ class: "px-4 text-sm" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['px-4']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-sm']} */ ;
    (e.method);
    __VLS_asFunctionalElement1(__VLS_intrinsics.td, __VLS_intrinsics.td)({
        ...{ class: "px-4 text-sm text-right font-mono" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['px-4']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-sm']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-right']} */ ;
    /** @type {__VLS_StyleScopedClasses['font-mono']} */ ;
    (e.es95);
    __VLS_asFunctionalElement1(__VLS_intrinsics.td, __VLS_intrinsics.td)({
        ...{ class: "px-4 text-sm text-right font-mono" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['px-4']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-sm']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-right']} */ ;
    /** @type {__VLS_StyleScopedClasses['font-mono']} */ ;
    (e.es99);
    // @ts-ignore
    [esResults,];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "mt-4 p-3 border" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['mt-4']} */ ;
/** @type {__VLS_StyleScopedClasses['p-3']} */ ;
/** @type {__VLS_StyleScopedClasses['border']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
    ...{ class: "text-xs" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-xs']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.strong, __VLS_intrinsics.strong)({
    ...{ style: {} },
});
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.h3, __VLS_intrinsics.h3)({
    ...{ class: "text-sm font-medium mb-3" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-sm']} */ ;
/** @type {__VLS_StyleScopedClasses['font-medium']} */ ;
/** @type {__VLS_StyleScopedClasses['mb-3']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
    width: "100%",
    height: "280",
    viewBox: "0 0 400 280",
    ...{ class: "w-full" },
});
/** @type {__VLS_StyleScopedClasses['w-full']} */ ;
for (const [i] of __VLS_vFor((6))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.line)({
        key: (`g-${i}),
'`), ': true,: x1, "80": ,
        y1: (30 + (i - 1) * 40),
        x2: "380",
        y2: (30 + (i - 1) * 40),
        stroke: "#333333",
        'stroke-width': "1",
        'stroke-dasharray': "4 4",
    });
    // @ts-ignore
    [];
}
for (const [i] of __VLS_vFor((6))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
        key: (`y-${i}),
'`), ': true,: x, "75": ,
        y: (30 + (i - 1) * 40 + 4),
        'text-anchor': "end",
        fill: "#666666",
        'font-size': "10",
    });
    (-(i - 1) * 2);
    // @ts-ignore
    [];
}
for (const [d, i] of __VLS_vFor((__VLS_ctx.varEsComparison))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.g, __VLS_intrinsics.g)({
        key: (i),
    });
    __VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
        x: (85 + i * 58),
        y: (30 + ((-d.var) / 12) * 200),
        width: "22",
        height: ((-d.var / 12) * 200),
        fill: "#FF6B00",
        opacity: "0.8",
    });
    __VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
        x: (110 + i * 58),
        y: (30 + ((-d.es) / 12) * 200),
        width: "22",
        height: ((-d.es / 12) * 200),
        fill: "#8B1A2B",
        opacity: "0.8",
    });
    __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
        x: (85 + i * 58 + 22),
        y: "255",
        'text-anchor': "middle",
        fill: "#666666",
        'font-size': "9",
    });
    (d.confidence);
    // @ts-ignore
    [varEsComparison,];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.g, __VLS_intrinsics.g)({
    transform: "translate(90, 270)",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
    x: "0",
    y: "-6",
    width: "12",
    height: "8",
    fill: "#FF6B00",
    opacity: "0.8",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
    x: "16",
    y: "0",
    fill: "#666666",
    'font-size': "9",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
    x: "50",
    y: "-6",
    width: "12",
    height: "8",
    fill: "#8B1A2B",
    opacity: "0.8",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
    x: "66",
    y: "0",
    fill: "#666666",
    'font-size': "9",
});
// @ts-ignore
[];
var __VLS_25;
const __VLS_28 = ScrollReveal || ScrollReveal;
// @ts-ignore
const __VLS_29 = __VLS_asFunctionalComponent1(__VLS_28, new __VLS_28({
    ...{ class: "mt-20" },
}));
const __VLS_30 = __VLS_29({
    ...{ class: "mt-20" },
}, ...__VLS_functionalComponentArgsRest(__VLS_29));
/** @type {__VLS_StyleScopedClasses['mt-20']} */ ;
const { default: __VLS_33 } = __VLS_31.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "w-full border" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['w-full']} */ ;
/** @type {__VLS_StyleScopedClasses['border']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "p-5 border-b" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['p-5']} */ ;
/** @type {__VLS_StyleScopedClasses['border-b']} */ ;
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
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "p-5" },
});
/** @type {__VLS_StyleScopedClasses['p-5']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4" },
});
/** @type {__VLS_StyleScopedClasses['grid']} */ ;
/** @type {__VLS_StyleScopedClasses['grid-cols-1']} */ ;
/** @type {__VLS_StyleScopedClasses['md:grid-cols-2']} */ ;
/** @type {__VLS_StyleScopedClasses['lg:grid-cols-3']} */ ;
/** @type {__VLS_StyleScopedClasses['gap-4']} */ ;
for (const [s] of __VLS_vFor((__VLS_ctx.stressScenarios))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ onClick: (...[$event]) => {
                __VLS_ctx.selectedScenario = __VLS_ctx.selectedScenario === s.id ? null : s.id;
                // @ts-ignore
                [stressScenarios, selectedScenario, selectedScenario,];
            } },
        key: (s.id),
        ...{ class: "border p-5 cursor-pointer transition-all duration-300 hover:border-opacity-100" },
        ...{ class: (__VLS_ctx.selectedScenario === s.id ? 'border-opacity-100' : '') },
        ...{ style: ({ borderColor: __VLS_ctx.selectedScenario === s.id ? '#8B1A2B' : '#333333', backgroundColor: __VLS_ctx.selectedScenario === s.id ? '#111111' : '#0A0A0A' }) },
    });
    /** @type {__VLS_StyleScopedClasses['border']} */ ;
    /** @type {__VLS_StyleScopedClasses['p-5']} */ ;
    /** @type {__VLS_StyleScopedClasses['cursor-pointer']} */ ;
    /** @type {__VLS_StyleScopedClasses['transition-all']} */ ;
    /** @type {__VLS_StyleScopedClasses['duration-300']} */ ;
    /** @type {__VLS_StyleScopedClasses['hover:border-opacity-100']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "flex items-center justify-between mb-3" },
    });
    /** @type {__VLS_StyleScopedClasses['flex']} */ ;
    /** @type {__VLS_StyleScopedClasses['items-center']} */ ;
    /** @type {__VLS_StyleScopedClasses['justify-between']} */ ;
    /** @type {__VLS_StyleScopedClasses['mb-3']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "text-sm font-medium" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['text-sm']} */ ;
    /** @type {__VLS_StyleScopedClasses['font-medium']} */ ;
    (s.name);
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "text-caption px-2 py-0.5 border" },
        ...{ style: ({ borderColor: __VLS_ctx.severityColor(s.severity), color: __VLS_ctx.severityColor(s.severity) }) },
    });
    /** @type {__VLS_StyleScopedClasses['text-caption']} */ ;
    /** @type {__VLS_StyleScopedClasses['px-2']} */ ;
    /** @type {__VLS_StyleScopedClasses['py-0.5']} */ ;
    /** @type {__VLS_StyleScopedClasses['border']} */ ;
    (__VLS_ctx.severityLabel(s.severity));
    __VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
        ...{ class: "text-xs mb-3" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['text-xs']} */ ;
    /** @type {__VLS_StyleScopedClasses['mb-3']} */ ;
    (s.description);
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "flex items-baseline gap-2" },
    });
    /** @type {__VLS_StyleScopedClasses['flex']} */ ;
    /** @type {__VLS_StyleScopedClasses['items-baseline']} */ ;
    /** @type {__VLS_StyleScopedClasses['gap-2']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "text-data" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['text-data']} */ ;
    (s.impact);
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "text-caption" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['text-caption']} */ ;
    if (__VLS_ctx.selectedScenario === s.id) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "mt-4 pt-4 border-t" },
            ...{ style: {} },
        });
        /** @type {__VLS_StyleScopedClasses['mt-4']} */ ;
        /** @type {__VLS_StyleScopedClasses['pt-4']} */ ;
        /** @type {__VLS_StyleScopedClasses['border-t']} */ ;
        for (const [d, i] of __VLS_vFor((s.details))) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
                key: (i),
                ...{ class: "flex justify-between items-center py-1.5" },
            });
            /** @type {__VLS_StyleScopedClasses['flex']} */ ;
            /** @type {__VLS_StyleScopedClasses['justify-between']} */ ;
            /** @type {__VLS_StyleScopedClasses['items-center']} */ ;
            /** @type {__VLS_StyleScopedClasses['py-1.5']} */ ;
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "text-xs" },
                ...{ style: {} },
            });
            /** @type {__VLS_StyleScopedClasses['text-xs']} */ ;
            (d.metric);
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                ...{ class: "text-xs font-mono" },
                ...{ style: {} },
            });
            /** @type {__VLS_StyleScopedClasses['text-xs']} */ ;
            /** @type {__VLS_StyleScopedClasses['font-mono']} */ ;
            (d.value);
            // @ts-ignore
            [selectedScenario, selectedScenario, selectedScenario, selectedScenario, severityColor, severityColor, severityLabel,];
        }
        __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
            ...{ class: "mt-3 flex flex-wrap gap-1" },
        });
        /** @type {__VLS_StyleScopedClasses['mt-3']} */ ;
        /** @type {__VLS_StyleScopedClasses['flex']} */ ;
        /** @type {__VLS_StyleScopedClasses['flex-wrap']} */ ;
        /** @type {__VLS_StyleScopedClasses['gap-1']} */ ;
        for (const [sector] of __VLS_vFor((s.affectedSectors))) {
            __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
                key: (sector),
                ...{ class: "text-caption px-2 py-0.5 border" },
                ...{ style: {} },
            });
            /** @type {__VLS_StyleScopedClasses['text-caption']} */ ;
            /** @type {__VLS_StyleScopedClasses['px-2']} */ ;
            /** @type {__VLS_StyleScopedClasses['py-0.5']} */ ;
            /** @type {__VLS_StyleScopedClasses['border']} */ ;
            (sector);
            // @ts-ignore
            [];
        }
    }
    // @ts-ignore
    [];
}
// @ts-ignore
[];
var __VLS_31;
const __VLS_34 = ScrollReveal || ScrollReveal;
// @ts-ignore
const __VLS_35 = __VLS_asFunctionalComponent1(__VLS_34, new __VLS_34({
    ...{ class: "mt-20" },
}));
const __VLS_36 = __VLS_35({
    ...{ class: "mt-20" },
}, ...__VLS_functionalComponentArgsRest(__VLS_35));
/** @type {__VLS_StyleScopedClasses['mt-20']} */ ;
const { default: __VLS_39 } = __VLS_37.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "w-full border" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['w-full']} */ ;
/** @type {__VLS_StyleScopedClasses['border']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "p-5 border-b" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['p-5']} */ ;
/** @type {__VLS_StyleScopedClasses['border-b']} */ ;
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
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "p-5" },
});
/** @type {__VLS_StyleScopedClasses['p-5']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "grid grid-cols-1 lg:grid-cols-2 gap-8" },
});
/** @type {__VLS_StyleScopedClasses['grid']} */ ;
/** @type {__VLS_StyleScopedClasses['grid-cols-1']} */ ;
/** @type {__VLS_StyleScopedClasses['lg:grid-cols-2']} */ ;
/** @type {__VLS_StyleScopedClasses['gap-8']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
    width: "100%",
    height: "280",
    viewBox: "0 0 500 280",
    ...{ class: "w-full" },
});
/** @type {__VLS_StyleScopedClasses['w-full']} */ ;
for (const [i] of __VLS_vFor((6))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.line)({
        key: (`g-${i}),
'`), ': true,: x1, "50": ,
        y1: (30 + (i - 1) * 40),
        x2: "480",
        y2: (30 + (i - 1) * 40),
        stroke: "#333333",
        'stroke-width': "1",
        'stroke-dasharray': "4 4",
    });
    // @ts-ignore
    [];
}
for (const [v] of __VLS_vFor(([5, 0, -5, -10, -15, -20]))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
        key: (v),
        x: "45",
        y: (30 + ((5 - v) / 25) * 40 + 4),
        'text-anchor': "end",
        fill: "#666666",
        'font-size': "10",
    });
    (v);
    // @ts-ignore
    [];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.line)({
    x1: "50",
    y1: "30 + (5/25)*200",
    x2: "480",
    y2: "30 + (5/25)*200",
    stroke: "#666666",
    'stroke-width': "2",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.line)({
    x1: "50",
    y1: (30 + ((5 - (-4.82)) / 25) * 200),
    x2: "480",
    y2: (30 + ((5 - (-4.82)) / 25) * 200),
    stroke: "#FF6B00",
    'stroke-width': "1",
    'stroke-dasharray': "4 4",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
    x: "485",
    y: (30 + ((5 - (-4.82)) / 25) * 200 + 3),
    fill: "#FF6B00",
    'font-size': "9",
});
for (const [d, i] of __VLS_vFor((__VLS_ctx.backtestData))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.g, __VLS_intrinsics.g)({
        key: (i),
    });
    __VLS_asFunctionalElement1(__VLS_intrinsics.line)({
        x1: (55 + i * 35),
        y1: (30 + ((5 - 0) / 25) * 200),
        x2: (55 + i * 35),
        y2: (30 + ((5 - d.actual) / 25) * 200),
        stroke: (d.breached ? '#8B1A2B' : d.actual >= 0 ? '#FFFFFF' : '#666666'),
        'stroke-width': "4",
    });
    if (d.breached) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
            x: (55 + i * 35),
            y: (30 + ((5 - d.actual) / 25) * 200 - 6),
            'text-anchor': "middle",
            fill: "#8B1A2B",
            'font-size': "8",
        });
    }
    // @ts-ignore
    [backtestData,];
}
for (const [d, i] of __VLS_vFor((__VLS_ctx.backtestData))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
        key: (`xl-${i}),
'`), ': true,: x
    }(55 + i * 35), y, "260", 'text-anchor', "middle", fill, "#666666", 'font-size', "8");
}
;
(d.date.slice(5));
// @ts-ignore
[backtestData,];
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "flex flex-col justify-center" },
});
/** @type {__VLS_StyleScopedClasses['flex']} */ ;
/** @type {__VLS_StyleScopedClasses['flex-col']} */ ;
/** @type {__VLS_StyleScopedClasses['justify-center']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "border p-5 mb-4" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['border']} */ ;
/** @type {__VLS_StyleScopedClasses['p-5']} */ ;
/** @type {__VLS_StyleScopedClasses['mb-4']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "grid grid-cols-2 gap-4" },
});
/** @type {__VLS_StyleScopedClasses['grid']} */ ;
/** @type {__VLS_StyleScopedClasses['grid-cols-2']} */ ;
/** @type {__VLS_StyleScopedClasses['gap-4']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-caption block mb-1" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-caption']} */ ;
/** @type {__VLS_StyleScopedClasses['block']} */ ;
/** @type {__VLS_StyleScopedClasses['mb-1']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-lg font-medium" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-lg']} */ ;
/** @type {__VLS_StyleScopedClasses['font-medium']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-caption block mb-1" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-caption']} */ ;
/** @type {__VLS_StyleScopedClasses['block']} */ ;
/** @type {__VLS_StyleScopedClasses['mb-1']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-lg font-medium" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-lg']} */ ;
/** @type {__VLS_StyleScopedClasses['font-medium']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-caption block mb-1" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-caption']} */ ;
/** @type {__VLS_StyleScopedClasses['block']} */ ;
/** @type {__VLS_StyleScopedClasses['mb-1']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-lg font-medium" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-lg']} */ ;
/** @type {__VLS_StyleScopedClasses['font-medium']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-caption block mb-1" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-caption']} */ ;
/** @type {__VLS_StyleScopedClasses['block']} */ ;
/** @type {__VLS_StyleScopedClasses['mb-1']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-lg font-medium" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-lg']} */ ;
/** @type {__VLS_StyleScopedClasses['font-medium']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-caption block mb-1" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-caption']} */ ;
/** @type {__VLS_StyleScopedClasses['block']} */ ;
/** @type {__VLS_StyleScopedClasses['mb-1']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-lg font-medium" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-lg']} */ ;
/** @type {__VLS_StyleScopedClasses['font-medium']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-caption block mb-1" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-caption']} */ ;
/** @type {__VLS_StyleScopedClasses['block']} */ ;
/** @type {__VLS_StyleScopedClasses['mb-1']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-lg font-medium" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-lg']} */ ;
/** @type {__VLS_StyleScopedClasses['font-medium']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
    ...{ class: "text-xs" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-xs']} */ ;
// @ts-ignore
[];
var __VLS_37;
const __VLS_40 = ScrollReveal || ScrollReveal;
// @ts-ignore
const __VLS_41 = __VLS_asFunctionalComponent1(__VLS_40, new __VLS_40({
    ...{ class: "mt-20 mb-20" },
}));
const __VLS_42 = __VLS_41({
    ...{ class: "mt-20 mb-20" },
}, ...__VLS_functionalComponentArgsRest(__VLS_41));
/** @type {__VLS_StyleScopedClasses['mt-20']} */ ;
/** @type {__VLS_StyleScopedClasses['mb-20']} */ ;
const { default: __VLS_45 } = __VLS_43.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "w-full border" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['w-full']} */ ;
/** @type {__VLS_StyleScopedClasses['border']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "p-5 border-b" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['p-5']} */ ;
/** @type {__VLS_StyleScopedClasses['border-b']} */ ;
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
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "p-5" },
});
/** @type {__VLS_StyleScopedClasses['p-5']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
    width: "100%",
    height: "400",
    viewBox: "0 0 900 400",
    ...{ class: "w-full" },
});
/** @type {__VLS_StyleScopedClasses['w-full']} */ ;
for (const [i] of __VLS_vFor((7))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.line)({
        key: (`g-${i}),
'`), ': true,: x1, "70": ,
        y1: (30 + (i - 1) * 50),
        x2: "850",
        y2: (30 + (i - 1) * 50),
        stroke: "#333333",
        'stroke-width': "1",
        'stroke-dasharray': "4 4",
    });
    // @ts-ignore
    [];
}
for (const [l] of __VLS_vFor((['+20%', '+10%', '0%', '-10%', '-20%', '-30%', '-40%']))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
        key: (l),
        x: "65",
        y: (30 + ({ '+20%': 0, '+10%': 1, '0%': 2, '-10%': 3, '-20%': 4, '-30%': 5, '-40%': 6 }[l]) * 50 + 4),
        'text-anchor': "end",
        fill: "#666666",
        'font-size': "10",
    });
    (l);
    // @ts-ignore
    [];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.polygon)({
    points: (__VLS_ctx.mcP1),
    fill: "#8B1A2B",
    'fill-opacity': "0.05",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.polygon)({
    points: (__VLS_ctx.mcP5),
    fill: "#8B1A2B",
    'fill-opacity': "0.08",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.polyline)({
    points: (__VLS_ctx.mcPaths[10].map((v, i) => `${70 + (i / 252) * 780},${200 - v * 400}`).join(' ')),
    fill: "none",
    stroke: "#FFFFFF",
    'stroke-width': "2",
});
for (const [path, idx] of __VLS_vFor((__VLS_ctx.displayedPaths))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.polyline)({
        key: (idx),
        points: (path.map((v, i) => `${70 + (i / 252) * 780},${200 - v * 400}`).join(' ')),
        fill: "none",
        stroke: (['#333333', '#444444', '#555555', '#666666'][idx % 4]),
        'stroke-width': "0.5",
        opacity: "0.4",
    });
    // @ts-ignore
    [mcP1, mcP5, mcPaths, displayedPaths,];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.g, __VLS_intrinsics.g)({
    transform: "translate(80, 18)",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.line)({
    x1: "0",
    y1: "0",
    x2: "20",
    y2: "0",
    stroke: "#FFFFFF",
    'stroke-width': "2",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
    x: "25",
    y: "4",
    fill: "#FFFFFF",
    'font-size': "10",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
    x: "100",
    y: "-6",
    width: "16",
    height: "10",
    fill: "#8B1A2B",
    'fill-opacity': "0.15",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
    x: "120",
    y: "4",
    fill: "#666666",
    'font-size': "10",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
    x: "180",
    y: "-6",
    width: "16",
    height: "10",
    fill: "#8B1A2B",
    'fill-opacity': "0.1",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
    x: "200",
    y: "4",
    fill: "#666666",
    'font-size': "10",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "grid grid-cols-2 lg:grid-cols-5 gap-4 mt-5" },
});
/** @type {__VLS_StyleScopedClasses['grid']} */ ;
/** @type {__VLS_StyleScopedClasses['grid-cols-2']} */ ;
/** @type {__VLS_StyleScopedClasses['lg:grid-cols-5']} */ ;
/** @type {__VLS_StyleScopedClasses['gap-4']} */ ;
/** @type {__VLS_StyleScopedClasses['mt-5']} */ ;
for (const [stat] of __VLS_vFor((__VLS_ctx.mcStats))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        key: (stat.label),
        ...{ class: "border p-3 text-center" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['border']} */ ;
    /** @type {__VLS_StyleScopedClasses['p-3']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-center']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "text-caption block mb-1" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['text-caption']} */ ;
    /** @type {__VLS_StyleScopedClasses['block']} */ ;
    /** @type {__VLS_StyleScopedClasses['mb-1']} */ ;
    (stat.label);
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "text-lg font-medium font-mono" },
        ...{ style: ({ color: stat.color }) },
    });
    /** @type {__VLS_StyleScopedClasses['text-lg']} */ ;
    /** @type {__VLS_StyleScopedClasses['font-medium']} */ ;
    /** @type {__VLS_StyleScopedClasses['font-mono']} */ ;
    (stat.value);
    // @ts-ignore
    [mcStats,];
}
// @ts-ignore
[];
var __VLS_43;
__VLS_asFunctionalElement1(__VLS_intrinsics.div)({
    ...{ class: "h-10" },
});
/** @type {__VLS_StyleScopedClasses['h-10']} */ ;
const __VLS_46 = Footer;
// @ts-ignore
const __VLS_47 = __VLS_asFunctionalComponent1(__VLS_46, new __VLS_46({}));
const __VLS_48 = __VLS_47({}, ...__VLS_functionalComponentArgsRest(__VLS_47));
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({});
export default {};
