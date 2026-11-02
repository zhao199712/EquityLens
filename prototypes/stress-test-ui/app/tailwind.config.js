/** @type {import('tailwindcss').Config} */
export default {
  content: ['./index.html', './src/**/*.{vue,js,ts,jsx,tsx}'],
  theme: {
    extend: {
      colors: {
        'bg-light': '#F5F5F5',
        'bg-dark': '#0A0A0A',
        'accent-red': '#8B1A2B',
        'accent-red-deep': '#6B1420',
        'text-gray': '#666666',
        'border-light': '#E0E0E0',
        'border-dark': '#333333',
        'accent-orange': '#FF6B00',
      },
      fontFamily: {
        heading: ['Noto Sans TC', 'sans-serif'],
        body: ['Inter', 'Noto Sans TC', 'sans-serif'],
        mono: ['Courier New', 'monospace'],
      },
      borderRadius: {
        none: '0px',
      },
    },
  },
  plugins: [],
}
