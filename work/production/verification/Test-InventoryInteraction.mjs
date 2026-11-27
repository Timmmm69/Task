// Run with Node and a chrome-headless-shell executable path as the first argument.
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { basename, join } from 'node:path';
import { pathToFileURL } from 'node:url';
import test from 'node:test';

const shell = process.argv[2] ?? '';
assert.match(basename(shell), /^chrome-headless-shell(?:\.exe)?$/i);
const source = readFileSync(new URL('../src/Task.Desktop/Inventory/MaterialInventory.html', import.meta.url), 'utf8');

function installFixture() {
    const material = { id: 'm', name: 'Материал', threshold: 5, unit: 'шт.' };
    const supplier = { id: 's', name: 'Поставщик <А>', phone: '123' };
    const movement = { id: 'r', date: '2020-01-01', type: 'receipt', supplierId: 's',
        lines: [{ id: 'line', materialId: 'm', quantity: 2, unitPrice: 3 }] };
    const details = { materials: [material], suppliers: [supplier], movements: [movement], units: ['шт.'] };
    const snapshot = { canManage: true, canAdminister: false, revision: 1, details, stock: [] };
    const bridge = new EventTarget();
    bridge.postMessage = ({ id, type }) => {
        if (type !== 'read') throw new Error('Unexpected write during test');
        queueMicrotask(() => bridge.dispatchEvent(new MessageEvent('message', { data: { id, result: { snapshot } } })));
    };
    window.chrome ??= {};
    window.chrome.webview = bridge;
}

async function exercise() {
    const find = (selector) => [...document.querySelectorAll(selector)].find(el => el.getClientRects().length);
    const click = (action) => find('[data-action="' + action + '"]').click();
    const check = (condition, message) => { if (!condition) throw new Error(message); };
    const set = (selector, value) => {
        const el = find(selector);
        el.value = value;
        el.dispatchEvent(new Event('input', { bubbles: true }));
    };
    const modalOpen = () => !document.querySelector('#overlay').classList.contains('hidden');
    const settle = () => new Promise(resolve => setTimeout(resolve, 60));
    try {
        await settle();
        set('#materialSearch', 'другой');
        click('show-warning-material');
        const row = find('[data-material="m"]');
        check(row?.getAttribute('aria-expanded') === 'true' && document.activeElement === row, 'Warning did not reveal its material');
        const supplier = find('[data-action="material-supplier"]');
        check(getComputedStyle(supplier).textDecorationLine.includes('underline'), 'Supplier link is not underlined');
        supplier.click();
        check(find('.supplier-value').textContent === 'Поставщик <А>' && !find('#supplier-name'), 'Read mode must display literal text');
        click('edit-supplier-card');
        check(find('#supplier-name').value === 'Поставщик <А>', 'Edit mode must restore the field');
        click('cancel-supplier-edit');
        click('close-modal');
        check(document.activeElement === supplier && row.getAttribute('aria-expanded') === 'true', 'Supplier return lost context');

        click('open-receipt');
        await settle();
        set('[data-draft-field="supplierId"]', 's');
        set('[data-draft-field="invoice"]', '123');
        set('[data-row-field="materialId"]', 'm');
        click('add-row');
        document.querySelector('#overlay').click();
        check(modalOpen() && find('[data-draft-field="invoice"]').value === '123', 'Backdrop discarded a draft');
        click('save-operation');
        check(document.activeElement === document.querySelectorAll('[data-row-field="materialId"]')[1], 'Invalid row is not focused');
        click('close-modal');
        click('open-issue');
        await settle();
        set('[data-row-field="materialId"]', 'm');
        set('[data-row-field="quantity"]', '3');
        click('save-operation');
        check(document.activeElement === find('[data-row-field="quantity"]'), 'Stock error is not attached to its quantity');
        document.querySelector('#overlay').click();
        check(modalOpen(), 'Backdrop discarded an issue');
        click('close-modal');
        click('open-receipt');
        document.querySelector('#overlay').click();
        check(!modalOpen(), 'Untouched draft did not dismiss');
        document.body.dataset.testResult = 'passed';
    } catch (error) {
        document.body.dataset.testResult = String(error);
    }
}

for (const width of [1440, 680]) {
    test('inventory interactions at width ' + width, () => {
        const directory = mkdtempSync(join(tmpdir(), 'task-inventory-test-'));
        try {
            const page = join(directory, 'fixture.html');
            const fixture = source.replace('<script>', '<script>(' + installFixture.toString() + ')();</script><script>')
                .replace('</body>', '<script>(' + exercise.toString() + ')();</script></body>');
            writeFileSync(page, fixture);
            const result = spawnSync(shell, ['--headless', '--disable-gpu', '--disable-background-networking',
                '--user-data-dir=' + join(directory, 'profile'), '--window-size=' + width + ',850', '--dump-dom',
                '--virtual-time-budget=3000', pathToFileURL(page).href], { encoding: 'utf8', windowsHide: true, timeout: 30000 });
            assert.ifError(result.error);
            assert.equal(result.status, 0, result.stderr);
            assert.equal(result.stdout.match(/<body\b[^>]*data-test-result="([^"]*)"/)?.[1], 'passed');
        } finally {
            rmSync(directory, { recursive: true, force: true, maxRetries: 3 });
        }
    });
}
