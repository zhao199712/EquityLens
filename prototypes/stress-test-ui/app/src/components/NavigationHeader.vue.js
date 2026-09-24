/// <reference types="../../../../../../home/kimi/.npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/template-helpers.d.ts" />
/// <reference types="../../../../../../home/kimi/.npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/props-fallback.d.ts" />
import { ref, computed, onMounted } from 'vue';
import { useRouter, useRoute } from 'vue-router';
import gsap from 'gsap';
const props = defineProps();
const router = useRouter();
const route = useRoute();
const titleRef = ref();
const subtitleRef = ref();
const navRef = ref();
const isDark = computed(() => props.isDark ?? false);
const currentPath = computed(() => route.path);
const navLinks = [
    { path: '/', label: '投資組合分析' },
    { path: '/financials', label: '財報分析' },
    { path: '/risk', label: '風險壓力測試' },
];
const pageConfig = computed(() => {
    switch (route.path) {
        case '/financials':
            return {
                mainTitle1: 'FINANCIAL',
                mainTitle2: 'STATEMENTS',
                subtitle: '深度解讀企業財務數據，透視營收結構與獲利能力，掌握財務健康趨勢',
            };
        case '/risk':
            return {
                mainTitle1: 'RISK',
                mainTitle2: 'ANALYSIS',
                subtitle: '全面評估投資組合風險暴露，VaR、ES量化分析與多重壓力情境測試',
            };
        default:
            return {
                mainTitle1: 'PORTFOLIO',
                mainTitle2: 'ANALYSIS',
                subtitle: '即時追蹤投資組合表現，智能分析收益與風險，數據驅動的資產配置洞察',
            };
    }
});
const mainTitle1 = computed(() => pageConfig.value.mainTitle1);
const mainTitle2 = computed(() => pageConfig.value.mainTitle2);
const subtitleText = computed(() => pageConfig.value.subtitle);
function navigateTo(path) {
    if (route.path === path)
        return;
    router.push(path);
}
onMounted(() => {
    const tl = gsap.timeline();
    if (titleRef.value) {
        tl.to(titleRef.value, {
            filter: 'blur(0px)',
            opacity: 1,
            duration: 1.2,
            ease: 'cubic-bezier(0.22, 1, 0.36, 1)',
        });
    }
    if (subtitleRef.value) {
        tl.to(subtitleRef.value, {
            opacity: 1,
            y: 0,
            duration: 0.8,
            ease: 'cubic-bezier(0.22, 1, 0.36, 1)',
        }, '-=0.4');
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
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "relative w-full flex flex-col items-center justify-center font-heading" },
    ...{ style: ({ backgroundColor: __VLS_ctx.isDark ? '#0A0A0A' : '#F5F5F5', color: __VLS_ctx.isDark ? '#FFFFFF' : '#000000', minHeight: '100vh', padding: 'clamp(20px, 4vw, 80px)' }) },
});
/** @type {__VLS_StyleScopedClasses['relative']} */ ;
/** @type {__VLS_StyleScopedClasses['w-full']} */ ;
/** @type {__VLS_StyleScopedClasses['flex']} */ ;
/** @type {__VLS_StyleScopedClasses['flex-col']} */ ;
/** @type {__VLS_StyleScopedClasses['items-center']} */ ;
/** @type {__VLS_StyleScopedClasses['justify-center']} */ ;
/** @type {__VLS_StyleScopedClasses['font-heading']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ref: "navRef",
    ...{ class: "flex flex-col items-center gap-8 w-full max-w-4xl" },
});
/** @type {__VLS_StyleScopedClasses['flex']} */ ;
/** @type {__VLS_StyleScopedClasses['flex-col']} */ ;
/** @type {__VLS_StyleScopedClasses['items-center']} */ ;
/** @type {__VLS_StyleScopedClasses['gap-8']} */ ;
/** @type {__VLS_StyleScopedClasses['w-full']} */ ;
/** @type {__VLS_StyleScopedClasses['max-w-4xl']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "flex flex-col items-center gap-1" },
});
/** @type {__VLS_StyleScopedClasses['flex']} */ ;
/** @type {__VLS_StyleScopedClasses['flex-col']} */ ;
/** @type {__VLS_StyleScopedClasses['items-center']} */ ;
/** @type {__VLS_StyleScopedClasses['gap-1']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "font-mono text-sm tracking-[0.3em]" },
    ...{ style: ({ color: __VLS_ctx.isDark ? '#FFFFFF' : '#000000' }) },
});
/** @type {__VLS_StyleScopedClasses['font-mono']} */ ;
/** @type {__VLS_StyleScopedClasses['text-sm']} */ ;
/** @type {__VLS_StyleScopedClasses['tracking-[0.3em]']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.span, __VLS_intrinsics.span)({
    ...{ class: "text-xs tracking-[0.2em] uppercase" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-xs']} */ ;
/** @type {__VLS_StyleScopedClasses['tracking-[0.2em]']} */ ;
/** @type {__VLS_StyleScopedClasses['uppercase']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ...{ class: "flex items-center gap-10" },
});
/** @type {__VLS_StyleScopedClasses['flex']} */ ;
/** @type {__VLS_StyleScopedClasses['items-center']} */ ;
/** @type {__VLS_StyleScopedClasses['gap-10']} */ ;
for (const [link] of __VLS_vFor((__VLS_ctx.navLinks))) {
    __VLS_asFunctionalElement1(__VLS_intrinsics.button, __VLS_intrinsics.button)({
        ...{ onClick: (...[$event]) => {
                __VLS_ctx.navigateTo(link.path);
                // @ts-ignore
                [isDark, isDark, isDark, navLinks, navigateTo,];
            } },
        key: (link.path),
        ...{ class: "text-xs font-medium tracking-[0.15em] uppercase transition-all duration-300 hover:-translate-y-1" },
        ...{ style: ({
                color: __VLS_ctx.currentPath === link.path ? (__VLS_ctx.isDark ? '#FFFFFF' : '#000000') : '#666666',
                fontWeight: __VLS_ctx.currentPath === link.path ? 700 : 500,
            }) },
    });
    /** @type {__VLS_StyleScopedClasses['text-xs']} */ ;
    /** @type {__VLS_StyleScopedClasses['font-medium']} */ ;
    /** @type {__VLS_StyleScopedClasses['tracking-[0.15em]']} */ ;
    /** @type {__VLS_StyleScopedClasses['uppercase']} */ ;
    /** @type {__VLS_StyleScopedClasses['transition-all']} */ ;
    /** @type {__VLS_StyleScopedClasses['duration-300']} */ ;
    /** @type {__VLS_StyleScopedClasses['hover:-translate-y-1']} */ ;
    (link.label);
    // @ts-ignore
    [isDark, currentPath, currentPath,];
}
__VLS_asFunctionalElement1(__VLS_intrinsics.div, __VLS_intrinsics.div)({
    ref: "titleRef",
    ...{ class: "flex flex-col items-center gap-0" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['flex']} */ ;
/** @type {__VLS_StyleScopedClasses['flex-col']} */ ;
/** @type {__VLS_StyleScopedClasses['items-center']} */ ;
/** @type {__VLS_StyleScopedClasses['gap-0']} */ ;
__VLS_asFunctionalElement1(__VLS_intrinsics.h1, __VLS_intrinsics.h1)({
    ...{ class: "text-display font-semibold tracking-tight text-center leading-none" },
    ...{ style: ({ color: __VLS_ctx.isDark ? '#FFFFFF' : '#000000' }) },
});
/** @type {__VLS_StyleScopedClasses['text-display']} */ ;
/** @type {__VLS_StyleScopedClasses['font-semibold']} */ ;
/** @type {__VLS_StyleScopedClasses['tracking-tight']} */ ;
/** @type {__VLS_StyleScopedClasses['text-center']} */ ;
/** @type {__VLS_StyleScopedClasses['leading-none']} */ ;
(__VLS_ctx.mainTitle1);
__VLS_asFunctionalElement1(__VLS_intrinsics.h1, __VLS_intrinsics.h1)({
    ...{ class: "text-display font-semibold tracking-tight text-center leading-none" },
    ...{ style: ({ color: __VLS_ctx.isDark ? '#FFFFFF' : '#000000' }) },
});
/** @type {__VLS_StyleScopedClasses['text-display']} */ ;
/** @type {__VLS_StyleScopedClasses['font-semibold']} */ ;
/** @type {__VLS_StyleScopedClasses['tracking-tight']} */ ;
/** @type {__VLS_StyleScopedClasses['text-center']} */ ;
/** @type {__VLS_StyleScopedClasses['leading-none']} */ ;
(__VLS_ctx.mainTitle2);
__VLS_asFunctionalElement1(__VLS_intrinsics.p, __VLS_intrinsics.p)({
    ref: "subtitleRef",
    ...{ class: "text-sm text-center max-w-md leading-relaxed" },
    ...{ style: {} },
});
/** @type {__VLS_StyleScopedClasses['text-sm']} */ ;
/** @type {__VLS_StyleScopedClasses['text-center']} */ ;
/** @type {__VLS_StyleScopedClasses['max-w-md']} */ ;
/** @type {__VLS_StyleScopedClasses['leading-relaxed']} */ ;
(__VLS_ctx.subtitleText);
// @ts-ignore
[isDark, isDark, mainTitle1, mainTitle2, subtitleText,];
const __VLS_export = (await import('vue')).defineComponent({
    __typeProps: {},
});
export default {};
