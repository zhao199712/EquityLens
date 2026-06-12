/// <reference types="../../../../../../home/kimi/.npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/template-helpers.d.ts" />
/// <reference types="../../../../../../home/kimi/.npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/props-fallback.d.ts" />
import { computed } from 'vue';
import { useRoute } from 'vue-router';
const route = useRoute();
const isDark = computed(() => route.path === '/financials' || route.path === '/risk');
const label = computed(() => {
    if (route.path === '/risk')
        return 'RISK';
    if (route.path === '/financials')
        return 'FINANCIALS';
    return 'PORTFOLIO';
});
const __VLS_ctx = {
    ...{},
    ...{},
};
let __VLS_components;
let __VLS_intrinsics;
let __VLS_directives;
__VLS_asFunctionalElement1(__VLS_intrinsics.footer, __VLS_intrinsics.footer)({
    ...{ class: "w-full flex items-center justify-between px-5" },
    ...{ style: ({
            backgroundColor: __VLS_ctx.isDark ? '#0A0A0A' : '#F5F5F5',
            borderTop: `1px solid ${__VLS_ctx.isDark ? '#333333' : '#E0E0E0'}`,
            height: '60px',
        }) },
});
/** @type {__VLS_StyleScopedClasses['w-full']} */ ;
/** @type {__VLS_StyleScopedClasses['flex']} */ ;
/** @type {__VLS_StyleScopedClasses['items-center']} */ ;
/** @type {__VLS_StyleScopedClasses['justify-between']} */ ;
/** @type {__VLS_StyleScopedClasses['px-5']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-xs" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-xs']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-xs font-mono tracking-wider uppercase" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-xs']} */ ;
/** @type {__VLS_StyleScopedClasses['font-mono']} */ ;
/** @type {__VLS_StyleScopedClasses['tracking-wider']} */ ;
/** @type {__VLS_StyleScopedClasses['uppercase']} */ ;
(__VLS_ctx.label);
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-xs" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-xs']} */ ;
// @ts-ignore
[isDark, isDark, label,];
const __VLS_export = (await import('vue')).defineComponent({});
export default {};
