/// <reference types="../../../../../../home/kimi/.npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/template-helpers.d.ts" />
/// <reference types="../../../../../../home/kimi/.npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/props-fallback.d.ts" />
import { ref, onMounted } from 'vue';
import gsap from 'gsap';
const props = defineProps();
const width = props.width ?? 300;
const height = props.height ?? 300;
const cx = width / 2;
const cy = height / 2;
const maxR = 110;
const levels = 5;
const angleStep = (Math.PI * 2) / props.dimensions.length;
const startAngle = -Math.PI / 2;
const fillColor = props.fillColor ?? '#8B1A2B';
const strokeColor = props.strokeColor ?? '#8B1A2B';
const axisPoints = props.dimensions.map((_, i) => {
    const a = startAngle + i * angleStep;
    return [cx + maxR * Math.cos(a), cy + maxR * Math.sin(a)];
});
const labelPoints = props.dimensions.map((_, i) => {
    const a = startAngle + i * angleStep;
    return [cx + (maxR + 22) * Math.cos(a), cy + (maxR + 22) * Math.sin(a)];
});
const gridPolys = Array.from({ length: levels }, (_, level) => {
    const r = ((level + 1) / levels) * maxR;
    return props.dimensions.map((_, i) => {
        const a = startAngle + i * angleStep;
        return [cx + r * Math.cos(a), cy + r * Math.sin(a)];
    });
});
const dataPoints = props.dimensions.map((d, i) => {
    const a = startAngle + i * angleStep;
    const r = (d.value / d.max) * maxR;
    return [cx + r * Math.cos(a), cy + r * Math.sin(a)];
});
const dataPolyStr = dataPoints.map(p => p.join(',')).join(' ');
const polyRef = ref();
onMounted(() => {
    if (!polyRef.value)
        return;
    gsap.fromTo(polyRef.value, { opacity: 0, scale: 0.5, transformOrigin: `${cx}px ${cy}px` }, {
        opacity: 1, scale: 1, duration: 1, ease: 'power2.out',
        scrollTrigger: { trigger: polyRef.value, start: 'top 85%' }
    });
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
    width: (__VLS_ctx.width),
    height: (__VLS_ctx.height),
    viewBox: (`0 0 ${__VLS_ctx.width} ${__VLS_ctx.height}`),
    ...{ class: "w-full" },
});
/** @type {__VLS_StyleScopedClasses['w-full']} */ ;
for (const [points, level] of __VLS_vFor((__VLS_ctx.gridPolys))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.polygon)({
        key: (`grid-${level}`),
        points: (points.map(p => p.join(',')).join(' ')),
        fill: "none",
        stroke: "#333333",
        'stroke-width': "1",
        'stroke-dasharray': "4 4",
    });
    // @ts-ignore
    [width, width, height, height, gridPolys,];
}
for (const [_, i] of __VLS_vFor((__VLS_ctx.dimensions))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.line)({
        key: (`axis-${i}`),
        x1: (__VLS_ctx.cx),
        y1: (__VLS_ctx.cy),
        x2: (__VLS_ctx.axisPoints[i][0]),
        y2: (__VLS_ctx.axisPoints[i][1]),
        stroke: "#333333",
        'stroke-width': "1",
    });
    // @ts-ignore
    [dimensions, cx, cy, axisPoints, axisPoints,];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.polygon)({
    ref: "polyRef",
    points: (__VLS_ctx.dataPolyStr),
    fill: (__VLS_ctx.fillColor),
    'fill-opacity': "0.2",
    stroke: (__VLS_ctx.strokeColor),
    'stroke-width': "2",
});
for (const [p, i] of __VLS_vFor((__VLS_ctx.dataPoints))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.circle)({
        key: (`dp-${i}`),
        cx: (p[0]),
        cy: (p[1]),
        r: "5",
        fill: (__VLS_ctx.fillColor),
    });
    // @ts-ignore
    [dataPolyStr, fillColor, fillColor, strokeColor, dataPoints,];
}
for (const [d, i] of __VLS_vFor((__VLS_ctx.dimensions))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
        key: (`lab-${i}`),
        x: (__VLS_ctx.labelPoints[i][0]),
        y: (__VLS_ctx.labelPoints[i][1] + 4),
        'text-anchor': "middle",
        fill: "#666666",
        'font-size': "11",
        'font-family': "Inter, sans-serif",
    });
    (d.label);
    // @ts-ignore
    [dimensions, labelPoints, labelPoints,];
}
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({
    __typeProps: {},
});
export default {};
