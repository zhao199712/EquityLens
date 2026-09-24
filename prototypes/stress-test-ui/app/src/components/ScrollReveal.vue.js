/// <reference types="../../../../../../home/kimi/.npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/template-helpers.d.ts" />
/// <reference types="../../../../../../home/kimi/.npm-cache/_npx/2db181330ea4b15b/node_modules/@vue/language-core/types/props-fallback.d.ts" />
import { ref, onMounted, computed } from 'vue';
import gsap from 'gsap';
import { ScrollTrigger } from 'gsap/ScrollTrigger';
gsap.registerPlugin(ScrollTrigger);
const props = defineProps();
const el = ref();
const style = computed(() => ({
    opacity: '0',
    transform: props.direction === 'left' ? 'translateX(-10px)' : 'translateY(30px)',
    ...props.style,
}));
onMounted(() => {
    if (!el.value)
        return;
    gsap.to(el.value, {
        opacity: 1,
        x: 0,
        y: 0,
        duration: props.duration ?? 0.8,
        delay: props.delay ?? 0,
        ease: 'cubic-bezier(0.22, 1, 0.36, 1)',
        scrollTrigger: {
            trigger: el.value,
            start: 'top 85%',
            toggleActions: 'play none none none',
        },
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
    ref: "el",
    ...{ class: "scroll-reveal" },
    ...{ class: (__VLS_ctx.$props.class) },
    ...{ style: (__VLS_ctx.style) },
});
/** @type {__VLS_StyleScopedClasses['scroll-reveal']} */ ;
var __VLS_0 = {};
// @ts-ignore
var __VLS_1 = __VLS_0;
// @ts-ignore
[$props, style,];
const __VLS_base = (await import('vue')).defineComponent({
    __typeProps: {},
});
const __VLS_export = {};
export default {};
