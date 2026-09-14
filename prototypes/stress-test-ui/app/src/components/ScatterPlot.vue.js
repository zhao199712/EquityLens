/// <reference types="../../../../../../home/kimi/.npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/template-helpers.d.ts" />
/// <reference types="../../../../../../home/kimi/.npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/props-fallback.d.ts" />
import { ref, computed } from 'vue';
const props = defineProps();
const hovered = ref(null);
const w = props.width ?? 300;
const h = props.height ?? 300;
const pad = { t: 20, r: 20, b: 50, l: 55 };
const chartW = w - pad.l - pad.r;
const chartH = h - pad.t - pad.b;
const xMin = props.xRange[0], xMax = props.xRange[1];
const yMin = props.yRange[0], yMax = props.yRange[1];
const sX = (v) => pad.l + ((v - xMin) / (xMax - xMin)) * chartW;
const sY = (v) => pad.t + chartH - ((v - yMin) / (yMax - yMin)) * chartH;
const frontierPath = computed(() => props.frontierCurve?.map(([x, y], i) => `${i === 0 ? 'M' : 'L'} ${sX(x)} ${sY(y)}`).join(' ') ?? '');
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
for (const [i] of __VLS_vFor((6))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.line)({
        key: (`xg-${i}`),
        x1: (__VLS_ctx.pad.l + ((i - 1) / 5) * __VLS_ctx.chartW),
        y1: (__VLS_ctx.pad.t),
        x2: (__VLS_ctx.pad.l + ((i - 1) / 5) * __VLS_ctx.chartW),
        y2: (__VLS_ctx.pad.t + __VLS_ctx.chartH),
        stroke: "#E0E0E0",
        'stroke-width': "1",
        'stroke-dasharray': "4 4",
    });
    // @ts-ignore
    [width, width, height, height, pad, pad, pad, pad, chartW, chartW, chartH,];
}
for (const [i] of __VLS_vFor((6))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.line)({
        key: (`yg-${i}`),
        x1: (__VLS_ctx.pad.l),
        y1: (__VLS_ctx.pad.t + ((i - 1) / 5) * __VLS_ctx.chartH),
        x2: (__VLS_ctx.pad.l + __VLS_ctx.chartW),
        y2: (__VLS_ctx.pad.t + ((i - 1) / 5) * __VLS_ctx.chartH),
        stroke: "#E0E0E0",
        'stroke-width': "1",
        'stroke-dasharray': "4 4",
    });
    // @ts-ignore
    [pad, pad, pad, pad, chartW, chartH, chartH,];
}
for (const [i] of __VLS_vFor((6))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
        key: (`xt-${i}`),
        x: (__VLS_ctx.pad.l + ((i - 1) / 5) * __VLS_ctx.chartW),
        y: (__VLS_ctx.height - 15),
        'text-anchor': "middle",
        fill: "#666666",
        'font-size': "10",
    });
    (Math.round(__VLS_ctx.xMin + ((i - 1) / 5) * (__VLS_ctx.xMax - __VLS_ctx.xMin)));
    // @ts-ignore
    [height, pad, chartW, xMin, xMin, xMax,];
}
for (const [i] of __VLS_vFor((6))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
        key: (`yt-${i}`),
        x: (__VLS_ctx.pad.l - 8),
        y: (__VLS_ctx.pad.t + __VLS_ctx.chartH - ((i - 1) / 5) * __VLS_ctx.chartH + 3),
        'text-anchor': "end",
        fill: "#666666",
        'font-size': "10",
    });
    (Math.round(__VLS_ctx.yMin + ((i - 1) / 5) * (__VLS_ctx.yMax - __VLS_ctx.yMin)));
    // @ts-ignore
    [pad, pad, chartH, chartH, yMin, yMin, yMax,];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
    x: (__VLS_ctx.pad.l + __VLS_ctx.chartW / 2),
    y: (__VLS_ctx.height - 2),
    'text-anchor': "middle",
    fill: "#666666",
    'font-size': "11",
});
(__VLS_ctx.xAxisLabel);
__VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
    x: "12",
    y: (__VLS_ctx.pad.t + __VLS_ctx.chartH / 2),
    'text-anchor': "middle",
    fill: "#666666",
    'font-size': "11",
    transform: (`rotate(-90, 12, ${__VLS_ctx.pad.t + __VLS_ctx.chartH / 2})`),
});
(__VLS_ctx.yAxisLabel);
if (__VLS_ctx.frontierCurve) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
        d: (__VLS_ctx.frontierPath),
        fill: "none",
        stroke: "#999999",
        'stroke-width': "1",
        'stroke-dasharray': "4 4",
    });
}
for (const [pt, i] of __VLS_vFor((__VLS_ctx.data))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.g, __VLS_intrinsics.g)({
        ...{ onMouseenter: (...[$event]) => {
                __VLS_ctx.hovered = i;
                // @ts-ignore
                [height, pad, pad, pad, chartW, chartH, chartH, xAxisLabel, yAxisLabel, frontierCurve, frontierPath, data, hovered,];
            } },
        ...{ onMouseleave: (...[$event]) => {
                __VLS_ctx.hovered = null;
                // @ts-ignore
                [hovered,];
            } },
        key: (`pt-${i}`),
        ...{ class: "cursor-pointer" },
    });
    /** @type {__VLS_StyleScopedClasses['cursor-pointer']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.circle)({
        cx: (__VLS_ctx.sX(pt.x)),
        cy: (__VLS_ctx.sY(pt.y)),
        r: (__VLS_ctx.hovered === i ? 10 : 6),
        fill: "#000000",
        ...{ class: "transition-all duration-200" },
    });
    /** @type {__VLS_StyleScopedClasses['transition-all']} */ ;
    /** @type {__VLS_StyleScopedClasses['duration-200']} */ ;
    if (__VLS_ctx.hovered === i) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.g, __VLS_intrinsics.g)({});
        __VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
            x: (__VLS_ctx.sX(pt.x) + 14),
            y: (__VLS_ctx.sY(pt.y) - 28),
            width: "140",
            height: "22",
            fill: "#000000",
        });
        __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
            x: (__VLS_ctx.sX(pt.x) + 19),
            y: (__VLS_ctx.sY(pt.y) - 12),
            fill: "#FFFFFF",
            'font-size': "11",
        });
        (pt.label);
        (pt.x);
        (pt.y);
    }
    // @ts-ignore
    [hovered, hovered, sX, sX, sX, sY, sY, sY,];
}
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({
    __typeProps: {},
});
export default {};
