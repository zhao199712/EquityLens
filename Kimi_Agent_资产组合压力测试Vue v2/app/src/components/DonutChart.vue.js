/// <reference types="../../../../../../home/kimi/.npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/template-helpers.d.ts" />
/// <reference types="../../../../../../home/kimi/.npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/props-fallback.d.ts" />
import { ref, computed } from 'vue';
const props = defineProps();
const emit = defineEmits();
const hovered = ref(null);
const width = props.width ?? 360;
const height = props.height ?? 360;
const cx = width / 2;
const cy = height / 2;
const outerR = 120;
const innerR = 75;
const total = props.segments.reduce((s, seg) => s + seg.value, 0);
const currentHover = computed(() => hovered.value !== null ? hovered.value : props.activeIndex);
const segmentPaths = computed(() => {
    let startAngle = -Math.PI / 2;
    return props.segments.map((seg) => {
        const angle = (seg.value / total) * Math.PI * 2;
        const endAngle = startAngle + angle;
        const midAngle = startAngle + angle / 2;
        const x1 = cx + outerR * Math.cos(startAngle);
        const y1 = cy + outerR * Math.sin(startAngle);
        const x2 = cx + outerR * Math.cos(endAngle);
        const y2 = cy + outerR * Math.sin(endAngle);
        const x3 = cx + innerR * Math.cos(endAngle);
        const y3 = cy + innerR * Math.sin(endAngle);
        const x4 = cx + innerR * Math.cos(startAngle);
        const y4 = cy + innerR * Math.sin(startAngle);
        const largeArc = angle > Math.PI ? 1 : 0;
        const d = `M ${x1} ${y1} A ${outerR} ${outerR} 0 ${largeArc} 1 ${x2} ${y2} L ${x3} ${y3} A ${innerR} ${innerR} 0 ${largeArc} 0 ${x4} ${y4} Z`;
        startAngle = endAngle;
        return { d, midAngle, data: seg };
    });
});
const __VLS_ctx = {
    ...{},
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
for (const [seg, i] of __VLS_vFor((__VLS_ctx.segmentPaths))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.g, __VLS_intrinsics.g)({
        key: (`seg-${i}`),
    });
    __VLS_asFunctionalElement1(__VLS_intrinsics.path)({
        ...{ onMouseenter: (...[$event]) => {
                __VLS_ctx.hovered = i;
                // @ts-ignore
                [width, width, height, height, segmentPaths, hovered,];
            } },
        ...{ onMouseleave: (...[$event]) => {
                __VLS_ctx.hovered = null;
                // @ts-ignore
                [hovered,];
            } },
        d: (seg.d),
        fill: (seg.data.color),
        stroke: (seg.data.borderColor || 'none'),
        'stroke-width': (seg.data.borderColor ? 1 : 0),
        transform: (__VLS_ctx.currentHover === i ? `translate(${5 * Math.cos(seg.midAngle)}, ${5 * Math.sin(seg.midAngle)})` : ''),
        ...{ class: "cursor-pointer transition-transform duration-300" },
    });
    /** @type {__VLS_StyleScopedClasses['cursor-pointer']} */ ;
    /** @type {__VLS_StyleScopedClasses['transition-transform']} */ ;
    /** @type {__VLS_StyleScopedClasses['duration-300']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.line)({
        x1: (__VLS_ctx.cx + __VLS_ctx.outerR * Math.cos(seg.midAngle)),
        y1: (__VLS_ctx.cy + __VLS_ctx.outerR * Math.sin(seg.midAngle)),
        x2: (__VLS_ctx.cx + (__VLS_ctx.outerR + 30) * Math.cos(seg.midAngle)),
        y2: (__VLS_ctx.cy + (__VLS_ctx.outerR + 30) * Math.sin(seg.midAngle)),
        stroke: "#E0E0E0",
        'stroke-width': "1",
    });
    __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
        x: (__VLS_ctx.cx + (__VLS_ctx.outerR + 40) * Math.cos(seg.midAngle)),
        y: (__VLS_ctx.cy + (__VLS_ctx.outerR + 40) * Math.sin(seg.midAngle) + 4),
        'text-anchor': (Math.cos(seg.midAngle) > 0 ? 'start' : 'end'),
        fill: "#666666",
        'font-size': "11",
        'font-family': "Inter, sans-serif",
    });
    (seg.data.label);
    (Math.round((seg.data.value / __VLS_ctx.total) * 100));
    // @ts-ignore
    [currentHover, cx, cx, cx, outerR, outerR, outerR, outerR, outerR, outerR, cy, cy, cy, total,];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
    x: (__VLS_ctx.cx),
    y: (__VLS_ctx.cy - 6),
    'text-anchor': "middle",
    fill: "#000000",
    'font-size': "20",
    'font-weight': "600",
    'font-family': "Noto Sans TC, sans-serif",
});
(__VLS_ctx.centerLabel);
if (__VLS_ctx.centerSubLabel) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
        x: (__VLS_ctx.cx),
        y: (__VLS_ctx.cy + 14),
        'text-anchor': "middle",
        fill: "#666666",
        'font-size': "11",
        'font-family': "Inter, sans-serif",
    });
    (__VLS_ctx.centerSubLabel);
}
// @ts-ignore
[cx, cx, cy, cy, centerLabel, centerSubLabel, centerSubLabel,];
const __VLS_export = (await import('vue')).defineComponent({
    __typeEmits: {},
    __typeProps: {},
});
export default {};
