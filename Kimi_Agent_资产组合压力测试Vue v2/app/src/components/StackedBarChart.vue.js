/// <reference types="../../../../../../home/kimi/.npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/template-helpers.d.ts" />
/// <reference types="../../../../../../home/kimi/.npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/props-fallback.d.ts" />
import { ref, onMounted } from 'vue';
import gsap from 'gsap';
const props = defineProps();
const width = props.width ?? 800;
const height = props.height ?? 380;
const pad = { t: 20, r: 60, b: 50, l: 60 };
const chartW = width - pad.l - pad.r;
const chartH = height - pad.t - pad.b;
const yLabels = props.yAxisLabels;
const maxV = Math.max(...props.data.map(d => d.values.reduce((s, v) => s + Math.max(0, v), 0)), ...(props.lineData ?? [0]));
const minV = Math.min(...props.data.map(d => d.values.reduce((s, v) => s + Math.min(0, v), 0)), ...(props.lineData ?? [0]));
const yRange = maxV - minV || 1;
const zeroY = pad.t + chartH - ((0 - minV) / yRange) * chartH;
const barGroupW = chartW / props.data.length;
const barW = Math.min(barGroupW * 0.5, 30);
function getBarY(v, vi, allVals) {
    let offset = 0;
    for (let i = 0; i < vi; i++) {
        offset += Math.abs((allVals[i] / yRange) * chartH);
    }
    return v >= 0 ? zeroY - offset - Math.abs((v / yRange) * chartH) : zeroY + offset;
}
const getLineY = (v) => pad.t + chartH - ((v - minV) / yRange) * chartH;
const svgRef = ref();
const lineRef = ref();
onMounted(() => {
    if (!svgRef.value)
        return;
    const bars = svgRef.value.querySelectorAll('.stack-bar');
    bars.forEach((bar, i) => {
        gsap.fromTo(bar, { scaleY: 0, transformOrigin: 'bottom' }, {
            scaleY: 1, duration: 0.8, delay: i * 0.05, ease: 'power2.out',
            scrollTrigger: { trigger: svgRef.value, start: 'top 80%' }
        });
    });
    if (lineRef.value && props.lineData) {
        const len = lineRef.value.getTotalLength();
        gsap.set(lineRef.value, { strokeDasharray: len, strokeDashoffset: len });
        gsap.to(lineRef.value, { strokeDashoffset: 0, duration: 1.5, ease: 'power2.out',
            scrollTrigger: { trigger: svgRef.value, start: 'top 80%' }
        });
    }
});
const __VLS_ctx = {
    ...{},
    ...{},
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
__VLS_asFunctionalElement1(__VLS_intrinsics.svg, __VLS_intrinsics.svg)({
    ref: "svgRef",
    width: (__VLS_ctx.width),
    height: (__VLS_ctx.height),
    viewBox: (`0 0 ${__VLS_ctx.width} ${__VLS_ctx.height}`),
    ...{ class: "w-full" },
});
/** @type {__VLS_StyleScopedClasses['w-full']} */ ;
for (const [i] of __VLS_vFor((__VLS_ctx.yLabels.length))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.line)({
        key: (`g-${i}`),
        x1: (__VLS_ctx.pad.l),
        y1: (__VLS_ctx.pad.t + ((i - 1) / (__VLS_ctx.yLabels.length - 1)) * __VLS_ctx.chartH),
        x2: (__VLS_ctx.width - __VLS_ctx.pad.r),
        y2: (__VLS_ctx.pad.t + ((i - 1) / (__VLS_ctx.yLabels.length - 1)) * __VLS_ctx.chartH),
        stroke: "#333333",
        'stroke-width': "1",
        'stroke-dasharray': "4 4",
    });
    // @ts-ignore
    [width, width, width, height, height, yLabels, yLabels, yLabels, pad, pad, pad, pad, chartH, chartH,];
}
for (const [l, i] of __VLS_vFor((__VLS_ctx.yLabels))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
        key: (`y-${i}`),
        x: (__VLS_ctx.pad.l - 10),
        y: (__VLS_ctx.pad.t + __VLS_ctx.chartH - ((i) / (__VLS_ctx.yLabels.length - 1)) * __VLS_ctx.chartH + 4),
        'text-anchor': "end",
        fill: "#666666",
        'font-size': "11",
    });
    (l);
    // @ts-ignore
    [yLabels, yLabels, pad, pad, chartH, chartH,];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.line)({
    x1: (__VLS_ctx.pad.l),
    y1: (__VLS_ctx.zeroY),
    x2: (__VLS_ctx.width - __VLS_ctx.pad.r),
    y2: (__VLS_ctx.zeroY),
    stroke: "#666666",
    'stroke-width': "2",
});
for (const [g, gi] of __VLS_vFor((__VLS_ctx.data))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.g, __VLS_intrinsics.g)({
        key: (`grp-${gi}`),
    });
    for (const [v, vi] of __VLS_vFor((g.values))) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
            key: (`b-${gi}-${vi}`),
            ...{ class: "stack-bar" },
            x: (__VLS_ctx.pad.l + gi * __VLS_ctx.barGroupW + __VLS_ctx.barGroupW / 2 - __VLS_ctx.barW / 2),
            y: (__VLS_ctx.getBarY(v, vi, g.values)),
            width: (__VLS_ctx.barW),
            height: (Math.abs((v / __VLS_ctx.yRange) * __VLS_ctx.chartH)),
            fill: (__VLS_ctx.colors[vi]),
        });
        /** @type {__VLS_StyleScopedClasses['stack-bar']} */ ;
        // @ts-ignore
        [width, pad, pad, pad, chartH, zeroY, zeroY, data, barGroupW, barGroupW, barW, barW, getBarY, yRange, colors,];
    }
    __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
        x: (__VLS_ctx.pad.l + gi * __VLS_ctx.barGroupW + __VLS_ctx.barGroupW / 2),
        y: (__VLS_ctx.height - 15),
        'text-anchor': "middle",
        fill: "#666666",
        'font-size': "10",
    });
    (g.label);
    // @ts-ignore
    [height, pad, barGroupW, barGroupW,];
}
if (__VLS_ctx.lineData) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.g, __VLS_intrinsics.g)({});
    __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
        ref: "lineRef",
        d: (__VLS_ctx.lineData.map((v, i) => `${i === 0 ? 'M' : 'L'} ${__VLS_ctx.pad.l + i * __VLS_ctx.barGroupW + __VLS_ctx.barGroupW / 2} ${__VLS_ctx.getLineY(v)}`).join(' ')),
        fill: "none",
        stroke: "#8B1A2B",
        'stroke-width': "2",
    });
    for (const [v, i] of __VLS_vFor((__VLS_ctx.lineData))) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.circle)({
            key: (`lp-${i}`),
            cx: (__VLS_ctx.pad.l + i * __VLS_ctx.barGroupW + __VLS_ctx.barGroupW / 2),
            cy: (__VLS_ctx.getLineY(v)),
            r: "4",
            fill: "#8B1A2B",
        });
        // @ts-ignore
        [pad, pad, barGroupW, barGroupW, barGroupW, barGroupW, lineData, lineData, lineData, getLineY, getLineY,];
    }
}
__VLS_asFunctionalElement1(__VLS_intrinsics.g, __VLS_intrinsics.g)({
    transform: (`translate(${__VLS_ctx.pad.l}, ${__VLS_ctx.height - 5})`),
});
for (const [l, i] of __VLS_vFor((__VLS_ctx.labels))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.g, __VLS_intrinsics.g)({
        key: (`leg-${i}`),
        transform: (`translate(${i * 120}, 0)`),
    });
    __VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
        x: (0),
        y: (-8),
        width: "10",
        height: "10",
        fill: (__VLS_ctx.colors[i]),
    });
    __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
        x: (16),
        y: (0),
        fill: "#666666",
        'font-size': "10",
    });
    (l);
    // @ts-ignore
    [height, pad, colors, labels,];
}
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({
    __typeProps: {},
});
export default {};
