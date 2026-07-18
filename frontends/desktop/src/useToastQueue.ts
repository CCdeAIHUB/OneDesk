import { ref } from "vue";

export function useToastQueue(timeoutMilliseconds = 3200, maximumVisible = 4) {
  const toasts = ref<Array<{ id: number; message: string }>>([]);
  let sequence = 0;

  function pushToast(message: string) {
    const id = ++sequence;
    toasts.value = [...toasts.value, { id, message }].slice(-maximumVisible);
    window.setTimeout(() => {
      toasts.value = toasts.value.filter((toast) => toast.id !== id);
    }, timeoutMilliseconds);
  }

  return { toasts, pushToast };
}
