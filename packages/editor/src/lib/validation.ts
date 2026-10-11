/**
 * Validation wrapper with debounce support.
 *
 * The validateCircuitGraph function from @circuit/schema runs purely
 * in the browser — no Node.js dependencies needed.
 */

import type { ValidationIssue } from "@circuit/schema";

let debounceTimer: ReturnType<typeof setTimeout> | null = null;

/**
 * Call `validateFn` after `ms` milliseconds of inactivity.
 * Returns a promise that resolves with the issues (or empty array on cancellation).
 */
export function debouncedValidate(
  validateFn: () => ValidationIssue[],
  ms: number = 300,
): Promise<ValidationIssue[]> {
  return new Promise((resolve) => {
    if (debounceTimer) clearTimeout(debounceTimer);
    debounceTimer = setTimeout(() => {
      debounceTimer = null;
      resolve(validateFn());
    }, ms);
  });
}

export function clearValidationDebounce(): void {
  if (debounceTimer) {
    clearTimeout(debounceTimer);
    debounceTimer = null;
  }
}
