/// <reference types="../../../../../../home/kimi/.npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/template-helpers.d.ts" />
/// <reference types="../../../../../../home/kimi/.npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/props-fallback.d.ts" />
import { ref, onMounted } from 'vue';
import gsap from 'gsap';
const props = defineProps();
const width = props.width ?? 400;
const barH = props.barHeight ?? 24;
const rowH = barH + 16;
const chartH = 10 + props.data.length * rowH + 10;
const labelW = 100;
const barMaxW = width - labelW - 80;
const maxVal = Math.max(...props.data.map(d => Math.abs(d.value)));
const textColor = props.textColor ?? '#000000';
const darkMode = props.darkMode ?? false;
const bars = ref([]);
const svgRef = ref();
onMounted(() => {
    bars.value.forEach((bar, i) => {
        if (!bar)
            return;
        const targetW = bar.getAttribute('width') || '0';
        gsap.set(bar, { attr: { width: 0 } });
        gsap.to(bar, { attr: { width: Number(targetW) }, duration: 0.8, delay: i * 0.1, ease: 'power2.out',
            scrollTrigger: { trigger: svgRef.value, start: 'top 85%' }
        });
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
    ref: "svgRef",
    width: (__VLS_ctx.width),
    height: (__VLS_ctx.chartH),
    viewBox: (`0 0 ${__VLS_ctx.width} ${__VLS_ctx.chartH}`),
});
for (const [item, i] of __VLS_vFor((__VLS_ctx.data))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.g, __VLS_intrinsics.g)({
        key: (`bar-${i}`),
    });
    __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
        x: (0),
        y: (10 + i * __VLS_ctx.rowH + __VLS_ctx.barH / 2 + 4),
        fill: (__VLS_ctx.darkMode ? '#FFFFFF' : '#000000'),
        'font-size': "12",
        'font-family': "Inter, sans-serif",
    });
    (item.label);
    __VLS_asFunctionalElement1(__VLS_intrinsics.rect)({
        ref: "bars",
        ...{ class: "bar-rect" },
        x: (__VLS_ctx.labelW),
        y: (10 + i * __VLS_ctx.rowH),
        width: ((Math.abs(item.value) / __VLS_ctx.maxVal) * __VLS_ctx.barMaxW),
        height: (__VLS_ctx.barH),
        fill: (item.color || (__VLS_ctx.darkMode ? '#FFFFFF' : '#000000')),
    });
    /** @type {__VLS_StyleScopedClasses['bar-rect']} */ ;
    __VLS_asFunctionalElement1(__VLS_intrinsics.text, __VLS_intrinsics.text)({
        x: (__VLS_ctx.labelW + ((Math.abs(item.value) / __VLS_ctx.maxVal) * __VLS_ctx.barMaxW) + 8),
        y: (10 + i * __VLS_ctx.rowH + __VLS_ctx.barH / 2 + 4),
        fill: (__VLS_ctx.textColor),
        'font-size': "12",
        'font-family': "Inter, sans-serif",
    });
    (item.value > 0 ? '+' : '');
    (item.value);
    // @ts-ignore
    [width, width, chartH, chartH, data, rowH, rowH, rowH, barH, barH, barH, darkMode, darkMode, labelW, labelW, maxVal, maxVal, barMaxW, barMaxW, textColor,];
}
// @ts-ignore
[];
const __VLS_export = (await import('vue')).defineComponent({
    __typeProps: {},
});
export default {};
