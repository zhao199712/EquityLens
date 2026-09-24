import NavigationHeader from '@/components/NavigationHeader.vue';
import Footer from '@/components/Footer.vue';
import ScrollReveal from '@/components/ScrollReveal.vue';
import DataTable from '@/components/DataTable.vue';
import StackedBarChart from '@/components/StackedBarChart.vue';
import RadarChart from '@/components/RadarChart.vue';
import ParticleCanvas from '@/components/ParticleCanvas.vue';
import { financialKPIData, profitabilityData, cashFlowData, radarData, ratioTableData, balanceSheetData, dupontData, revCombined } from '@/data/financialsData';
const profitCards = [
    { title: '毛利率趨勢', data: profitabilityData.grossMargin, base: 35, range: 10, color: '#FFFFFF' },
    { title: '營業利益率', data: profitabilityData.operatingMargin, base: 10, range: 10, color: '#8B1A2B' },
    { title: '淨利率', data: profitabilityData.netMargin, base: 15, range: 10, color: '#666666' },
];
const dupontFactors = [
    { label: '淨利率', value: dupontData.netMargin },
    { label: '資產周轉率', value: dupontData.assetTurnover },
    { label: '權益乘數', value: dupontData.equityMultiplier },
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
for (const [kpi, i] of __VLS_vFor((__VLS_ctx.financialKPIData))) {
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
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['text-data']} */ ;
    (kpi.value);
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "text-xs mt-1" },
        ...{ style: ({ color: kpi.positive ? '#8B1A2B' : '#666666' }) },
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
    [financialKPIData,];
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
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "flex gap-3 mt-4 sm:mt-0" },
});
/** @type {__VLS_StyleScopedClasses['flex']} */ ;
/** @type {__VLS_StyleScopedClasses['gap-3']} */ ;
/** @type {__VLS_StyleScopedClasses['mt-4']} */ ;
/** @type {__VLS_StyleScopedClasses['sm:mt-0']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-xs cursor-pointer" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-xs']} */ ;
/** @type {__VLS_StyleScopedClasses['cursor-pointer']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-xs cursor-pointer" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-xs']} */ ;
/** @type {__VLS_StyleScopedClasses['cursor-pointer']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "p-5" },
});
/** @type {__VLS_StyleScopedClasses['p-5']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
    width: "100%",
    height: "420",
    viewBox: "0 0 900 420",
    ...{ class: "w-full" },
});
/** @type {__VLS_StyleScopedClasses['w-full']} */ ;
for (const [i] of __VLS_vFor((6))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.line)({
        key: (`g-${i}`),
        x1: (80),
        y1: (40 + ((i - 1) / 5) * 320),
        x2: (860),
        y2: (40 + ((i - 1) / 5) * 320),
        stroke: "#333333",
        'stroke-width': "1",
        'stroke-dasharray': "4 4",
    });
    // @ts-ignore
    [];
}
for (const [l, i] of __VLS_vFor((['100B', '80B', '60B', '40B', '20B', '0']))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
        key: (`y-${i}`),
        x: "75",
        y: (40 + (i / 5) * 320 + 4),
        'text-anchor': "end",
        fill: "#666666",
        'font-size': "11",
    });
    (l);
    // @ts-ignore
    [];
}
for (const [d, i] of __VLS_vFor((__VLS_ctx.revCombined))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.g, __VLS_intrinsics.g)({
        key: (`bg-${i}`),
    });
    if (d.r23) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
            ...{ class: "bar-anim" },
            x: (80 + (i / 12) * 780 - 16),
            y: (360 - (d.r23 / 100) * 320),
            width: "16",
            height: ((d.r23 / 100) * 320),
            fill: "#333333",
        });
        /** @type {__VLS_StyleScopedClasses['bar-anim']} */ ;
    }
    if (d.r24) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
            ...{ class: "bar-anim" },
            x: (80 + (i / 12) * 780),
            y: (360 - (d.r24 / 100) * 320),
            width: "16",
            height: ((d.r24 / 100) * 320),
            fill: "#666666",
        });
        /** @type {__VLS_StyleScopedClasses['bar-anim']} */ ;
    }
    if (d.r25) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
            ...{ class: "bar-anim" },
            x: (80 + (i / 12) * 780 + 18),
            y: (360 - (d.r25 / 100) * 320),
            width: "16",
            height: ((d.r25 / 100) * 320),
            fill: "#FFFFFF",
        });
        /** @type {__VLS_StyleScopedClasses['bar-anim']} */ ;
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
        x: (80 + (i / 12) * 780),
        y: "385",
        'text-anchor': "middle",
        fill: "#666666",
        'font-size': "10",
    });
    (d.q);
    // @ts-ignore
    [revCombined,];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.polyline)({
    points: (__VLS_ctx.revCombined.map((d, i) => `${i === 0 ? 'M' : 'L'} ${80 + (i / 12) * 780} ${360 - (d.ni / 100) * 320}`).join(' ')),
    fill: "none",
    stroke: "#8B1A2B",
    'stroke-width': "2",
});
for (const [d, i] of __VLS_vFor((__VLS_ctx.revCombined))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.circle)({
        key: (`ni-${i}),
'`), ': true,: cx
    }(80 + (i / 12) * 780), cy, (360 - (d.ni / 100) * 320), r, "4", fill, "#8B1A2B");
}
;
// @ts-ignore
[revCombined, revCombined,];
__VLS_asFunctionalElement1(__VLS_intrinsics.g, __VLS_intrinsics.g)({
    transform: "translate(350, 410)",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
    x: "0",
    y: "-8",
    width: "12",
    height: "10",
    fill: "#333333",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
    x: "18",
    y: "0",
    fill: "#666666",
    'font-size': "10",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
    x: "90",
    y: "-8",
    width: "12",
    height: "10",
    fill: "#666666",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
    x: "108",
    y: "0",
    fill: "#666666",
    'font-size': "10",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
    x: "180",
    y: "-8",
    width: "12",
    height: "10",
    fill: "#FFFFFF",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
    x: "198",
    y: "0",
    fill: "#666666",
    'font-size': "10",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.circle)({
    cx: "285",
    cy: "-3",
    r: "4",
    fill: "#8B1A2B",
});
__VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
    x: "295",
    y: "0",
    fill: "#666666",
    'font-size': "10",
});
// @ts-ignore
[];
var __VLS_19;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "mt-20 grid grid-cols-1 md:grid-cols-3 gap-3" },
});
/** @type {__VLS_StyleScopedClasses['mt-20']} */ ;
/** @type {__VLS_StyleScopedClasses['grid']} */ ;
/** @type {__VLS_StyleScopedClasses['grid-cols-1']} */ ;
/** @type {__VLS_StyleScopedClasses['md:grid-cols-3']} */ ;
/** @type {__VLS_StyleScopedClasses['gap-3']} */ ;
for (const [card, i] of __VLS_vFor((__VLS_ctx.profitCards))) {
    const __VLS_22 = ScrollReveal || ScrollReveal;
    // @ts-ignore
    const __VLS_23 = __VLS_asFunctionalComponent1(__VLS_22, new __VLS_22({
        key: (i),
        delay: (i * 0.15),
    }));
    const __VLS_24 = __VLS_23({
        key: (i),
        delay: (i * 0.15),
    }, ...__VLS_functionalComponentArgsRest(__VLS_23));
    const { default: __VLS_27 } = __VLS_25.slots;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "border p-5" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['border']} */ ;
    /** @type {__VLS_StyleScopedClasses['p-5']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.h3, __VLS_intrinsics.h3)({
        ...{ class: "font-heading text-lg font-medium mb-4" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['font-heading']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-lg']} */ ;
    /** @type {__VLS_StyleScopedClasses['font-medium']} */ ;
    /** @type {__VLS_StyleScopedClasses['mb-4']} */ ;
    (card.title);
    __VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
        width: "100%",
        height: (160),
        viewBox: (`0 0 300 160`),
        ...{ class: "w-full" },
    });
    /** @type {__VLS_StyleScopedClasses['w-full']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.polyline)({
        points: (card.data.map((v, i) => `${20 + (i / 11) * 260},${140 - ((v - card.base) / card.range) * 120}`).join(' ')),
        fill: "none",
        stroke: (card.color),
        'stroke-width': "2",
    });
    for (const [v, idx] of __VLS_vFor((card.data))) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.circle)({
            key: (idx),
            cx: (20 + (idx / 11) * 260),
            cy: (140 - ((v - card.base) / card.range) * 120),
            r: "3",
            fill: (card.color),
        });
        // @ts-ignore
        [profitCards,];
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
        x: "280",
        y: (140 - ((card.data[card.data.length - 1] - card.base) / card.range) * 120 + 4),
        'text-anchor': "end",
        fill: "#FFFFFF",
        'font-size': "14",
        'font-weight': "600",
    });
    (card.data[card.data.length - 1]);
    // @ts-ignore
    [];
    var __VLS_25;
    // @ts-ignore
    [];
}
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
const __VLS_34 = StackedBarChart;
// @ts-ignore
const __VLS_35 = __VLS_asFunctionalComponent1(__VLS_34, new __VLS_34({
    data: (__VLS_ctx.cashFlowData.labels.map((label, i) => ({ label, values: [__VLS_ctx.cashFlowData.operating[i], __VLS_ctx.cashFlowData.investing[i], __VLS_ctx.cashFlowData.financing[i]] }))),
    colors: (['#FFFFFF', '#666666', '#333333']),
    labels: (['營業活動', '投資活動', '籌資活動', '自由現金流']),
    yAxisLabels: (['-10B', '0', '10B', '20B', '30B']),
    lineData: (__VLS_ctx.cashFlowData.freeCashFlow),
}));
const __VLS_36 = __VLS_35({
    data: (__VLS_ctx.cashFlowData.labels.map((label, i) => ({ label, values: [__VLS_ctx.cashFlowData.operating[i], __VLS_ctx.cashFlowData.investing[i], __VLS_ctx.cashFlowData.financing[i]] }))),
    colors: (['#FFFFFF', '#666666', '#333333']),
    labels: (['營業活動', '投資活動', '籌資活動', '自由現金流']),
    yAxisLabels: (['-10B', '0', '10B', '20B', '30B']),
    lineData: (__VLS_ctx.cashFlowData.freeCashFlow),
}, ...__VLS_functionalComponentArgsRest(__VLS_35));
// @ts-ignore
[cashFlowData, cashFlowData, cashFlowData, cashFlowData, cashFlowData,];
var __VLS_31;
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
const __VLS_39 = ScrollReveal || ScrollReveal;
// @ts-ignore
const __VLS_40 = __VLS_asFunctionalComponent1(__VLS_39, new __VLS_39({
    ...{ class: "p-5 flex flex-col items-center" },
    ...{ style: {} },
}));
const __VLS_41 = __VLS_40({
    ...{ class: "p-5 flex flex-col items-center" },
    ...{ style: {} },
}, ...__VLS_functionalComponentArgsRest(__VLS_40));
/** @type {__VLS_StyleScopedClasses['p-5']} */ ;
/** @type {__VLS_StyleScopedClasses['flex']} */ ;
/** @type {__VLS_StyleScopedClasses['flex-col']} */ ;
/** @type {__VLS_StyleScopedClasses['items-center']} */ ;
const { default: __VLS_44 } = __VLS_42.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.h2, __VLS_intrinsics.h2)({
    ...{ class: "font-heading text-2xl font-semibold mb-6" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['font-heading']} */ ;
/** @type {__VLS_StyleScopedClasses['text-2xl']} */ ;
/** @type {__VLS_StyleScopedClasses['font-semibold']} */ ;
/** @type {__VLS_StyleScopedClasses['mb-6']} */ ;
const __VLS_45 = RadarChart;
// @ts-ignore
const __VLS_46 = __VLS_asFunctionalComponent1(__VLS_45, new __VLS_45({
    dimensions: (__VLS_ctx.radarData),
}));
const __VLS_47 = __VLS_46({
    dimensions: (__VLS_ctx.radarData),
}, ...__VLS_functionalComponentArgsRest(__VLS_46));
// @ts-ignore
[radarData,];
var __VLS_42;
const __VLS_50 = ScrollReveal || ScrollReveal;
// @ts-ignore
const __VLS_51 = __VLS_asFunctionalComponent1(__VLS_50, new __VLS_50({
    ...{ class: "p-5" },
}));
const __VLS_52 = __VLS_51({
    ...{ class: "p-5" },
}, ...__VLS_functionalComponentArgsRest(__VLS_51));
/** @type {__VLS_StyleScopedClasses['p-5']} */ ;
const { default: __VLS_55 } = __VLS_53.slots;
__VLS_asFunctionalElement1(__VLS_intrinsics.h2, __VLS_intrinsics.h2)({
    ...{ class: "font-heading text-2xl font-semibold mb-4" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['font-heading']} */ ;
/** @type {__VLS_StyleScopedClasses['text-2xl']} */ ;
/** @type {__VLS_StyleScopedClasses['font-semibold']} */ ;
/** @type {__VLS_StyleScopedClasses['mb-4']} */ ;
const __VLS_56 = DataTable;
// @ts-ignore
const __VLS_57 = __VLS_asFunctionalComponent1(__VLS_56, new __VLS_56({
    headers: (['比率名稱', '當期', '上期', '變動', '趨勢']),
    rows: (__VLS_ctx.ratioTableData.map(r => [r.name, r.current, r.prev, r.change, r.trend === 'up' ? '↑' : '↓'])),
    darkMode: (true),
    compact: (true),
}));
const __VLS_58 = __VLS_57({
    headers: (['比率名稱', '當期', '上期', '變動', '趨勢']),
    rows: (__VLS_ctx.ratioTableData.map(r => [r.name, r.current, r.prev, r.change, r.trend === 'up' ? '↑' : '↓'])),
    darkMode: (true),
    compact: (true),
}, ...__VLS_functionalComponentArgsRest(__VLS_57));
// @ts-ignore
[ratioTableData,];
var __VLS_53;
const __VLS_61 = ScrollReveal || ScrollReveal;
// @ts-ignore
const __VLS_62 = __VLS_asFunctionalComponent1(__VLS_61, new __VLS_61({
    ...{ class: "mt-20" },
}));
const __VLS_63 = __VLS_62({
    ...{ class: "mt-20" },
}, ...__VLS_functionalComponentArgsRest(__VLS_62));
/** @type {__VLS_StyleScopedClasses['mt-20']} */ ;
const { default: __VLS_66 } = __VLS_64.slots;
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
    ...{ class: "grid grid-cols-1 lg:grid-cols-2" },
});
/** @type {__VLS_StyleScopedClasses['grid']} */ ;
/** @type {__VLS_StyleScopedClasses['grid-cols-1']} */ ;
/** @type {__VLS_StyleScopedClasses['lg:grid-cols-2']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "p-5" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['p-5']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.h3, __VLS_intrinsics.h3)({
    ...{ class: "text-sm font-medium mb-4" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-sm']} */ ;
/** @type {__VLS_StyleScopedClasses['font-medium']} */ ;
/** @type {__VLS_StyleScopedClasses['mb-4']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "flex h-10 w-full mb-4" },
});
/** @type {__VLS_StyleScopedClasses['flex']} */ ;
/** @type {__VLS_StyleScopedClasses['h-10']} */ ;
/** @type {__VLS_StyleScopedClasses['w-full']} */ ;
/** @type {__VLS_StyleScopedClasses['mb-4']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div)({
    ...{ class: "h-full" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['h-full']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div)({
    ...{ class: "h-full" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['h-full']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div)({
    ...{ class: "h-full" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['h-full']} */ ;
for (const [a, i] of __VLS_vFor((__VLS_ctx.balanceSheetData.assets))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        key: (i),
        ...{ class: "flex items-center gap-2" },
    });
    /** @type {__VLS_StyleScopedClasses['flex']} */ ;
    /** @type {__VLS_StyleScopedClasses['items-center']} */ ;
    /** @type {__VLS_StyleScopedClasses['gap-2']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div)({
        ...{ class: "w-3 h-3" },
        ...{ style: ({ backgroundColor: a.color }) },
    });
    /** @type {__VLS_StyleScopedClasses['w-3']} */ ;
    /** @type {__VLS_StyleScopedClasses['h-3']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "text-sm flex-1" },
        ...{ style: ({ color: a.color === '#FFFFFF' ? '#FFFFFF' : '#999999' }) },
    });
    /** @type {__VLS_StyleScopedClasses['text-sm']} */ ;
    /** @type {__VLS_StyleScopedClasses['flex-1']} */ ;
    (a.label);
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "text-sm" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['text-sm']} */ ;
    (a.value);
    // @ts-ignore
    [balanceSheetData,];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "p-5" },
});
/** @type {__VLS_StyleScopedClasses['p-5']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.h3, __VLS_intrinsics.h3)({
    ...{ class: "text-sm font-medium mb-4" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-sm']} */ ;
/** @type {__VLS_StyleScopedClasses['font-medium']} */ ;
/** @type {__VLS_StyleScopedClasses['mb-4']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "flex h-10 w-full mb-4" },
});
/** @type {__VLS_StyleScopedClasses['flex']} */ ;
/** @type {__VLS_StyleScopedClasses['h-10']} */ ;
/** @type {__VLS_StyleScopedClasses['w-full']} */ ;
/** @type {__VLS_StyleScopedClasses['mb-4']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div)({
    ...{ class: "h-full" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['h-full']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div)({
    ...{ class: "h-full" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['h-full']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div)({
    ...{ class: "h-full" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['h-full']} */ ;
for (const [l, i] of __VLS_vFor((__VLS_ctx.balanceSheetData.liabilities))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        key: (i),
        ...{ class: "flex items-center gap-2" },
    });
    /** @type {__VLS_StyleScopedClasses['flex']} */ ;
    /** @type {__VLS_StyleScopedClasses['items-center']} */ ;
    /** @type {__VLS_StyleScopedClasses['gap-2']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div)({
        ...{ class: "w-3 h-3" },
        ...{ style: ({ backgroundColor: l.color }) },
    });
    /** @type {__VLS_StyleScopedClasses['w-3']} */ ;
    /** @type {__VLS_StyleScopedClasses['h-3']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "text-sm flex-1" },
        ...{ style: ({ color: l.color === '#FFFFFF' ? '#FFFFFF' : '#999999' }) },
    });
    /** @type {__VLS_StyleScopedClasses['text-sm']} */ ;
    /** @type {__VLS_StyleScopedClasses['flex-1']} */ ;
    (l.label);
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "text-sm" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['text-sm']} */ ;
    (l.value);
    // @ts-ignore
    [balanceSheetData,];
}
// @ts-ignore
[];
var __VLS_64;
const __VLS_67 = ScrollReveal || ScrollReveal;
// @ts-ignore
const __VLS_68 = __VLS_asFunctionalComponent1(__VLS_67, new __VLS_67({
    ...{ class: "mt-20 mb-20" },
}));
const __VLS_69 = __VLS_68({
    ...{ class: "mt-20 mb-20" },
}, ...__VLS_functionalComponentArgsRest(__VLS_68));
/** @type {__VLS_StyleScopedClasses['mt-20']} */ ;
/** @type {__VLS_StyleScopedClasses['mb-20']} */ ;
const { default: __VLS_72 } = __VLS_70.slots;
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
    ...{ class: "flex flex-col md:flex-row items-center justify-center gap-4 md:gap-8 mb-8" },
});
/** @type {__VLS_StyleScopedClasses['flex']} */ ;
/** @type {__VLS_StyleScopedClasses['flex-col']} */ ;
/** @type {__VLS_StyleScopedClasses['md:flex-row']} */ ;
/** @type {__VLS_StyleScopedClasses['items-center']} */ ;
/** @type {__VLS_StyleScopedClasses['justify-center']} */ ;
/** @type {__VLS_StyleScopedClasses['gap-4']} */ ;
/** @type {__VLS_StyleScopedClasses['md:gap-8']} */ ;
/** @type {__VLS_StyleScopedClasses['mb-8']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "border p-5 text-center" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['border']} */ ;
/** @type {__VLS_StyleScopedClasses['p-5']} */ ;
/** @type {__VLS_StyleScopedClasses['text-center']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-caption block mb-1" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-caption']} */ ;
/** @type {__VLS_StyleScopedClasses['block']} */ ;
/** @type {__VLS_StyleScopedClasses['mb-1']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-data" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-data']} */ ;
(__VLS_ctx.dupontData.roe);
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-2xl" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-2xl']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "flex flex-col sm:flex-row items-center gap-3" },
});
/** @type {__VLS_StyleScopedClasses['flex']} */ ;
/** @type {__VLS_StyleScopedClasses['flex-col']} */ ;
/** @type {__VLS_StyleScopedClasses['sm:flex-row']} */ ;
/** @type {__VLS_StyleScopedClasses['items-center']} */ ;
/** @type {__VLS_StyleScopedClasses['gap-3']} */ ;
for (const [f, i] of __VLS_vFor((__VLS_ctx.dupontFactors))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        key: (i),
        ...{ class: "flex items-center gap-3" },
    });
    /** @type {__VLS_StyleScopedClasses['flex']} */ ;
    /** @type {__VLS_StyleScopedClasses['items-center']} */ ;
    /** @type {__VLS_StyleScopedClasses['gap-3']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
        ...{ class: "border p-4 text-center" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['border']} */ ;
    /** @type {__VLS_StyleScopedClasses['p-4']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-center']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "text-caption block mb-1" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['text-caption']} */ ;
    /** @type {__VLS_StyleScopedClasses['block']} */ ;
    /** @type {__VLS_StyleScopedClasses['mb-1']} */ ;
    (f.label);
    __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
        ...{ class: "text-lg font-medium" },
        ...{ style: {} },
    });
    /** @type {__VLS_StyleScopedClasses['text-lg']} */ ;
    /** @type {__VLS_StyleScopedClasses['font-medium']} */ ;
    (f.value);
    if (i < 2) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
            ...{ class: "text-lg" },
            ...{ style: {} },
        });
        /** @type {__VLS_StyleScopedClasses['text-lg']} */ ;
    }
    // @ts-ignore
    [dupontData, dupontFactors,];
}
const __VLS_73 = DataTable;
// @ts-ignore
const __VLS_74 = __VLS_asFunctionalComponent1(__VLS_73, new __VLS_73({
    headers: (['指標', '當期', '同期業平均', '差異']),
    rows: (__VLS_ctx.dupontData.table.map(r => [r.metric, r.current, r.industry, r.diff])),
    darkMode: (true),
}));
const __VLS_75 = __VLS_74({
    headers: (['指標', '當期', '同期業平均', '差異']),
    rows: (__VLS_ctx.dupontData.table.map(r => [r.metric, r.current, r.industry, r.diff])),
    darkMode: (true),
}, ...__VLS_functionalComponentArgsRest(__VLS_74));
// @ts-ignore
[dupontData,];
var __VLS_70;
__VLS_asFunctionalElement1(__VLS_intrinsics.div)({
    ...{ class: "h-10" },
});
/** @type {__VLS_StyleScopedClasses['h-10']} */ ;
const __VLS_78 = Footer;
// @ts-ignore
const __VLS_79 = __VLS_asFunctionalComponent1(__VLS_78, new __VLS_78({}));
const __VLS_80 = __VLS_79({}, ...__VLS_functionalComponentArgsRest(__VLS_79));
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({});
export default {};
