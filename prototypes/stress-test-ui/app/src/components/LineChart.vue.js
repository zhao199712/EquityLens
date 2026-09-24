/// <reference types="../../../../../../home/kimi/.npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/template-helpers.d.ts" />
/// <reference types="../../../../../../home/kimi/.npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/props-fallback.d.ts" />
import { ref, onMounted } from 'vue';
import gsap from 'gsap';
import { ScrollTrigger } from 'gsap/ScrollTrigger';
gsap.registerPlugin(ScrollTrigger);
const props = defineProps();
const svgRef = ref();
const pathRef = ref();
const areaRef = ref();
const secondPathRef = ref();
const width = props.width ?? 800;
const height = props.height ?? 400;
const padding = { top: 20, right: 20, bottom: 40, left: 70 };
const chartW = width - padding.left - padding.right;
const chartH = height - padding.top - padding.bottom;
const lineColor = props.lineColor ?? '#000000';
const gridColor = props.gridColor ?? '#E0E0E0';
const textColor = props.textColor ?? '#666666';
const secondLineColor = props.secondLineColor ?? '#8B1A2B';
const allVals = [...props.data, ...(props.secondLine ?? [])];
const maxVal = Math.max(...allVals);
const minVal = Math.min(...allVals);
const range = maxVal - minVal || 1;
const getX = (i) => padding.left + (i / (props.data.length - 1)) * chartW;
const getY = (v) => padding.top + chartH - ((v - minVal) / range) * chartH;
const linePath = props.data.map((v, i) => `${i === 0 ? 'M' : 'L'} ${getX(i)} ${getY(v)}`).join(' ');
const areaPath = `${linePath} L ${getX(props.data.length - 1)} ${padding.top + chartH} L ${getX(0)} ${padding.top + chartH} Z`;
const secondLinePath = props.secondLine?.map((v, i) => `${i === 0 ? 'M' : 'L'} ${getX(i)} ${getY(v)}`).join(' ') ?? '';
onMounted(() => {
    if (!svgRef.value)
        return;
    const tl = gsap.timeline({
        scrollTrigger: { trigger: svgRef.value, start: 'top 80%', toggleActions: 'play none none none' }
    });
    if (pathRef.value) {
        const len = pathRef.value.getTotalLength();
        gsap.set(pathRef.value, { strokeDasharray: len, strokeDashoffset: len });
        tl.to(pathRef.value, { strokeDashoffset: 0, duration: 2, ease: 'power2.out' });
    }
    if (secondPathRef.value && props.secondLine) {
        const len = secondPathRef.value.getTotalLength();
        gsap.set(secondPathRef.value, { strokeDasharray: len, strokeDashoffset: len });
        tl.to(secondPathRef.value, { strokeDashoffset: 0, duration: 1.5, ease: 'power2.out' }, '-=1');
    }
    if (areaRef.value && props.showArea) {
        gsap.set(areaRef.value, { opacity: 0 });
        tl.to(areaRef.value, { opacity: 1, duration: 1 }, '-=1');
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
    viewBox: (`0 0 ${__VLS_ctx.width} ${__VLS_ctx.height}`),
    ...{ class: "w-full" },
    preserveAspectRatio: "xMidYMid meet",
});
/** @type {__VLS_StyleScopedClasses['w-full']} */ ;
for (const [_, i] of __VLS_vFor((__VLS_ctx.yAxisLabels))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.line)({
        key: (`grid-${i}`),
        x1: (__VLS_ctx.padding.left),
        y1: (__VLS_ctx.padding.top + (i / (__VLS_ctx.yAxisLabels.length - 1)) * __VLS_ctx.chartH),
        x2: (__VLS_ctx.width - __VLS_ctx.padding.right),
        y2: (__VLS_ctx.padding.top + (i / (__VLS_ctx.yAxisLabels.length - 1)) * __VLS_ctx.chartH),
        stroke: (__VLS_ctx.gridColor),
        'stroke-width': "1",
        'stroke-dasharray': "4 4",
    });
    // @ts-ignore
    [width, width, height, yAxisLabels, yAxisLabels, yAxisLabels, padding, padding, padding, padding, chartH, chartH, gridColor,];
}
for (const [label, i] of __VLS_vFor((__VLS_ctx.yAxisLabels))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
        key: (`y-${i}`),
        x: (__VLS_ctx.padding.left - 10),
        y: (__VLS_ctx.padding.top + __VLS_ctx.chartH - (i / (__VLS_ctx.yAxisLabels.length - 1)) * __VLS_ctx.chartH + 4),
        'text-anchor': "end",
        fill: (__VLS_ctx.textColor),
        'font-size': "11",
        'font-family': "Inter, sans-serif",
    });
    (label);
    // @ts-ignore
    [yAxisLabels, yAxisLabels, padding, padding, chartH, chartH, textColor,];
}
for (const [label, i] of __VLS_vFor((__VLS_ctx.labels))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
        key: (`x-${i}`),
        x: (__VLS_ctx.getX(i)),
        y: (__VLS_ctx.height - 10),
        'text-anchor': "middle",
        fill: (__VLS_ctx.textColor),
        'font-size': "11",
        'font-family': "Inter, sans-serif",
    });
    (label);
    // @ts-ignore
    [height, textColor, labels, getX,];
}
if (__VLS_ctx.showArea) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
        ref: "areaRef",
        d: (__VLS_ctx.areaPath),
        fill: (__VLS_ctx.fillColor || (__VLS_ctx.darkMode ? 'rgba(255,255,255,0.03)' : 'rgba(0,0,0,0.03)')),
        opacity: "0",
    });
}
__VLS_asFunctionalElement1(__VLS_intrinsics.path)({
    ref: "pathRef",
    d: (__VLS_ctx.linePath),
    fill: "none",
    stroke: (__VLS_ctx.lineColor),
    'stroke-width': "2",
});
if (__VLS_ctx.secondLine) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
        ref: "secondPathRef",
        d: (__VLS_ctx.secondLinePath),
        fill: "none",
        stroke: (__VLS_ctx.secondLineColor),
        'stroke-width': "2",
    });
}
for (const [v, i] of __VLS_vFor((__VLS_ctx.data))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.circle)({
        key: (`pt-${i}`),
        cx: (__VLS_ctx.getX(i)),
        cy: (__VLS_ctx.getY(v)),
        r: "4",
        fill: (__VLS_ctx.lineColor),
        ...{ class: "cursor-pointer hover:r-6 transition-all" },
    });
    /** @type {__VLS_StyleScopedClasses['cursor-pointer']} */ ;
    /** @type {__VLS_StyleScopedClasses['hover:r-6']} */ ;
    /** @type {__VLS_StyleScopedClasses['transition-all']} */ ;
    // @ts-ignore
    [getX, showArea, areaPath, fillColor, darkMode, linePath, lineColor, lineColor, secondLine, secondLinePath, secondLineColor, data, getY,];
}
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({
    __typeProps: {},
});
export default {};
