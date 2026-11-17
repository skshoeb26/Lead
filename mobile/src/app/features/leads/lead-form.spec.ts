import { describe, expect, it } from 'vitest';
import { PHONE_PATTERN, PIN_PATTERN } from './lead-form';
import { statusClass } from '../approvals/approval-list';

describe('lead form validation patterns', () => {
  it('accepts Indian phone formats', () => {
    for (const p of ['+91 98765 43210', '09876543210', '022-2345 6789']) expect(PHONE_PATTERN.test(p)).toBe(true);
    for (const p of ['abc', '12']) expect(PHONE_PATTERN.test(p)).toBe(false);
  });
  it('validates 6-digit PIN codes', () => {
    expect(PIN_PATTERN.test('400001')).toBe(true);
    expect(PIN_PATTERN.test('012345')).toBe(false);
    expect(PIN_PATTERN.test('4000')).toBe(false);
  });
});

describe('approval status presentation', () => {
  it('flags states that need attention', () => {
    expect(statusClass('OutcomeUnknown')).toBe('bad');
    expect(statusClass('Failed')).toBe('bad');
    expect(statusClass('Sent')).toBe('ok');
    expect(statusClass('PendingApproval')).toBe('warn');
  });
});
