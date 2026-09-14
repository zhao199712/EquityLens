/// <reference types="../../../../../../home/kimi/.npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/template-helpers.d.ts" />
/// <reference types="../../../../../../home/kimi/.npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/props-fallback.d.ts" />
import { ref, onMounted } from 'vue';
import gsap from 'gsap';
import { ScrollTrigger } from 'gsap/ScrollTrigger';
gsap.registerPlugin(ScrollTrigger);
const props = defineProps();
const tableRef = ref();
function getCellColor(cell, colIdx, rowIdx, rowLen) {
    if (props.highlightRow === rowIdx)
        return '#FFFFFF';
    if (colIdx === rowLen - 1 && typeof cell === 'string' && cell.startsWith('+'))
        return props.darkMode ? '#FFFFFF' : '#000000';
    if (colIdx === rowLen - 1 && typeof cell === 'string' && cell.startsWith('-'))
        return '#666666';
    return props.darkMode ? '#FFFFFF' : '#000000';
}
onMounted(() => {
    if (!tableRef.value)
        return;
    const rowEls = tableRef.value.querySelectorAll('.data-row');
    rowEls.forEach((row, i) => {
        gsap.fromTo(row, { opacity: 0, x: -10 }, {
            opacity: 1, x: 0, duration: 0.5, delay: i * 0.05, ease: 'power2.out',
            scrollTrigger: { trigger: tableRef.value, start: 'top 85%' }
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
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ref: "tableRef",
    ...{ class: "w-full overflow-x-auto" },
});
/** @type {__VLS_StyleScopedClasses['w-full']} */ ;
/** @type {__VLS_StyleScopedClasses['overflow-x-auto']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.table, __VLS_intrinsics.table)({
    ...{ class: "w-full" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['w-full']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.thead, __VLS_intrinsics.thead)({});
__VLS_asFunctionalElement1(__VLS_intrinsics.tr, __VLS_intrinsics.tr)({
    ...{ style: ({ backgroundColor: __VLS_ctx.darkMode ? '#333333' : '#000000' }) },
});
for (const [h, i] of __VLS_vFor((__VLS_ctx.headers))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.th, __VLS_intrinsics.th)({
        key: (`h-${i}`),
        ...{ class: "text-caption uppercase text-left px-4" },
        ...{ style: ({ color: '#FFFFFF', height: __VLS_ctx.compact ? 40 : 48, borderBottom: `1px solid ${__VLS_ctx.darkMode ? '#333333' : '#E0E0E0'}`, whiteSpace: 'nowrap', fontSize: '11px', letterSpacing: '0.1em' }) },
    });
    /** @type {__VLS_StyleScopedClasses['text-caption']} */ ;
    /** @type {__VLS_StyleScopedClasses['uppercase']} */ ;
    /** @type {__VLS_StyleScopedClasses['text-left']} */ ;
    /** @type {__VLS_StyleScopedClasses['px-4']} */ ;
    (h);
    // @ts-ignore
    [darkMode, darkMode, headers, compact,];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.tbody, __VLS_intrinsics.tbody)({});
for (const [row, rowIdx] of __VLS_vFor((__VLS_ctx.rows))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.tr, __VLS_intrinsics.tr)({
        ...{ onMouseenter: (...[$event]) => {
                __VLS_ctx.onRowHover?.(rowIdx);
                // @ts-ignore
                [rows, onRowHover,];
            } },
        ...{ onMouseleave: (...[$event]) => {
                __VLS_ctx.onRowHover?.(null);
                // @ts-ignore
                [onRowHover,];
            } },
        key: (`r-${rowIdx}`),
        ...{ class: "data-row transition-colors duration-200" },
        ...{ style: ({
                backgroundColor: __VLS_ctx.highlightRow === rowIdx ? (__VLS_ctx.darkMode ? '#1A1A1A' : '#000000')
                    : (rowIdx % 2 === 0 ? (__VLS_ctx.darkMode ? '#0A0A0A' : '#FFFFFF') : (__VLS_ctx.darkMode ? '#111111' : '#F5F5F5')),
                height: __VLS_ctx.compact ? 40 : 48,
                cursor: __VLS_ctx.onRowHover ? 'pointer' : 'default',
            }) },
    });
    /** @type {__VLS_StyleScopedClasses['data-row']} */ ;
    /** @type {__VLS_StyleScopedClasses['transition-colors']} */ ;
    /** @type {__VLS_StyleScopedClasses['duration-200']} */ ;
    for (const [cell, colIdx] of __VLS_vFor((row))) {
        __VLS_asFunctionalElement1(__VLS_intrinsics.td, __VLS_intrinsics.td)({
            key: (`c-${rowIdx}-${colIdx}`),
            ...{ class: "px-4 text-sm whitespace-nowrap" },
            ...{ style: ({
                    color: __VLS_ctx.getCellColor(cell, colIdx, rowIdx, row.length),
                    fontWeight: colIdx === 0 ? 600 : 400,
                    borderBottom: `1px solid ${__VLS_ctx.darkMode ? '#333333' : '#E0E0E0'}`,
                }) },
        });
        /** @type {__VLS_StyleScopedClasses['px-4']} */ ;
        /** @type {__VLS_StyleScopedClasses['text-sm']} */ ;
        /** @type {__VLS_StyleScopedClasses['whitespace-nowrap']} */ ;
        var __VLS_0 = {
            cell: (cell),
            row: (row),
            rowIdx: (rowIdx),
            colIdx: (colIdx),
        };
        var __VLS_1 = __VLS_tryAsConstant(`cell-${colIdx}`);
        (typeof cell === 'number' ? cell.toLocaleString() : cell);
        // @ts-ignore
        [darkMode, darkMode, darkMode, darkMode, compact, onRowHover, highlightRow, getCellColor,];
    }
    // @ts-ignore
    [];
}
// @ts-ignore
var __VLS_2 = __VLS_1, __VLS_3 = __VLS_0;
// @ts-ignore
[];
const __VLS_base = (await import('vue')).defineComponent({
    __typeProps: {},
});
const __VLS_export = {};
export default {};
