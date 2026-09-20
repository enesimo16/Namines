// @vitest-environment jsdom
import { describe, it, expect, afterEach, beforeEach } from 'vitest';
import { render, screen, cleanup, fireEvent } from '@testing-library/react';
import AutomationRuleDrawer from './AutomationRuleDrawer';
import { useAutomationStore } from '../../store/useAutomationStore';
import { useSchemaStore } from '../../store/useSchemaStore';

describe('AutomationRuleDrawer', () => {
  beforeEach(() => {
    useAutomationStore.setState({ rules: [], selectedRuleId: null });
    // Kapsam seçici tabloları şemadan okuyor; şemasız bir canvas'ta zaten
    // kural kurulamıyor, bu yüzden testler de gerçekçi bir şema ile çalışıyor.
    useSchemaStore.setState({
      schema: {
        schemaId: 's1',
        name: 'test',
        tables: [{ id: 't-orders', name: 'orders', columns: [] }],
        relations: [],
      },
    } as never);
  });
  afterEach(() => cleanup());

  it('renders nothing when no rule is selected', () => {
    render(<AutomationRuleDrawer />);
    expect(screen.queryByText(/Namines Flow —/)).not.toBeInTheDocument();
  });

  it('shows the Namines Flow brand in its title, with the trigger scope', () => {
    const id = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Webhook');
    useAutomationStore.getState().setSelectedRuleId(id);

    render(<AutomationRuleDrawer />);

    expect(screen.getByText(/Namines Flow/)).toBeInTheDocument();
  });

  // I2: AutomationRuleMatcher, RelationAdded/RelationDeleted'ı YALNIZCA proje
  // geneli kurallar (ScopeTableId == null) için eşleştiriyor, ama kural
  // oluşturmanın tek yolu (CanvasContextMenu) her zaman bir tablo id'si
  // veriyor — bu iki seçenek hiçbir kuralda tetiklenemezdi.
  it('does not offer relation triggers that can never fire for a table-scoped rule', () => {
    const id = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Webhook');
    useAutomationStore.getState().setSelectedRuleId(id);

    render(<AutomationRuleDrawer />);

    const triggerSelect = screen.getByLabelText(/trigger/i) as HTMLSelectElement;
    const offered = Array.from(triggerSelect.options).map(o => o.value);

    expect(offered).not.toContain('RelationAdded');
    expect(offered).not.toContain('RelationDeleted');
    // Tablo kapsamlı kuralların gerçekten tetiklenebildiği seçenekler duruyor.
    expect(offered).toEqual(['TableAdded', 'TableDeleted', 'ColumnAdded', 'ColumnDeleted', 'ColumnChanged']);
  });

  it('changing the action to Webhook reveals a URL field, and typing in it saves', () => {
    const id = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Webhook');
    useAutomationStore.getState().setSelectedRuleId(id);

    render(<AutomationRuleDrawer />);

    const urlInput = screen.getByLabelText(/webhook url/i);
    fireEvent.change(urlInput, { target: { value: 'https://example.com/hook' } });
    fireEvent.blur(urlInput);

    expect(useAutomationStore.getState().rules.find(r => r.id === id)?.actions[0].actionConfig.url)
      .toBe('https://example.com/hook');
  });

  it('switching the action away from Webhook hides the URL field', () => {
    const id = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Toast');
    useAutomationStore.getState().setSelectedRuleId(id);

    render(<AutomationRuleDrawer />);

    expect(screen.queryByLabelText(/webhook url/i)).not.toBeInTheDocument();
  });

  it('proje geneline gecince iliski tetikleyicileri aciliyor', () => {
    const id = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Toast');
    useAutomationStore.getState().setSelectedRuleId(id);
    render(<AutomationRuleDrawer />);

    fireEvent.change(screen.getByLabelText(/scope/i), { target: { value: '__project__' } });

    const offered = Array.from((screen.getByLabelText(/trigger/i) as HTMLSelectElement).options).map(o => o.value);
    expect(offered).toContain('RelationAdded');
    expect(offered).toContain('RelationDeleted');
    expect(useAutomationStore.getState().rules.find(r => r.id === id)?.scopeTableId).toBe('');
  });

  it('tablo kapsamina donerken iliski tetikleyicisi gecerli bir degere cekiliyor', () => {
    // Aksi hâlde kural sessizce asla tetiklenemez hâle gelirdi.
    const id = useAutomationStore.getState().addRule('', 'RelationAdded', 'Toast');
    useAutomationStore.getState().setSelectedRuleId(id);
    render(<AutomationRuleDrawer />);

    fireEvent.change(screen.getByLabelText(/scope/i), { target: { value: 't-orders' } });

    const saved = useAutomationStore.getState().rules.find(r => r.id === id);
    expect(saved?.scopeTableId).toBe('t-orders');
    expect(saved?.triggerType).toBe('TableDeleted');
  });

  it('kosul eklenip degeri kaydediliyor', () => {
    const id = useAutomationStore.getState().addRule('t-orders', 'ColumnDeleted', 'Toast');
    useAutomationStore.getState().setSelectedRuleId(id);
    render(<AutomationRuleDrawer />);

    fireEvent.click(screen.getByLabelText(/add condition/i));
    fireEvent.change(screen.getByLabelText(/condition 1 field/i), { target: { value: 'columnName' } });
    fireEvent.change(screen.getByLabelText(/condition 1 operator/i), { target: { value: 'endsWith' } });
    const value = screen.getByLabelText(/condition 1 value/i);
    fireEvent.change(value, { target: { value: '_id' } });
    fireEvent.blur(value);

    expect(useAutomationStore.getState().rules.find(r => r.id === id)?.conditions).toEqual([
      { uid: expect.any(String), field: 'columnName', op: 'endsWith', value: '_id' },
    ]);
  });

  it('iliski tetikleyicisinde kosul bolumu sunulmuyor', () => {
    // Olay tablo/kolon adı taşımadığı için yazılan her koşul kuralı ölü
    // hâle getirirdi.
    const id = useAutomationStore.getState().addRule('', 'RelationAdded', 'Toast');
    useAutomationStore.getState().setSelectedRuleId(id);
    render(<AutomationRuleDrawer />);

    expect(screen.queryByLabelText(/add condition/i)).not.toBeInTheDocument();
  });

  it('aksiyon eklenip sirasi degistirilebiliyor', () => {
    const id = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Toast');
    useAutomationStore.getState().setSelectedRuleId(id);
    render(<AutomationRuleDrawer />);

    fireEvent.click(screen.getByLabelText(/add action/i));
    fireEvent.change(screen.getByLabelText(/^action 2$/i), { target: { value: 'Webhook' } });

    expect(useAutomationStore.getState().rules.find(r => r.id === id)?.actions.map(a => a.actionType))
      .toEqual(['Toast', 'Webhook']);

    fireEvent.click(screen.getByLabelText(/move action 2 up/i));

    expect(useAutomationStore.getState().rules.find(r => r.id === id)?.actions.map(a => a.actionType))
      .toEqual(['Webhook', 'Toast']);
  });

  it('aksiyon silinebiliyor', () => {
    const id = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Toast');
    useAutomationStore.getState().setSelectedRuleId(id);
    render(<AutomationRuleDrawer />);

    fireEvent.click(screen.getByLabelText(/remove action 1/i));

    expect(useAutomationStore.getState().rules.find(r => r.id === id)?.actions).toEqual([]);
  });

  it('kural adi kaydediliyor', () => {
    const id = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Toast');
    useAutomationStore.getState().setSelectedRuleId(id);
    render(<AutomationRuleDrawer />);

    const name = screen.getByLabelText(/flow name/i);
    fireEvent.change(name, { target: { value: 'Kritik tablo bekcisi' } });
    fireEvent.blur(name);

    expect(useAutomationStore.getState().rules.find(r => r.id === id)?.name).toBe('Kritik tablo bekcisi');
  });

  it('enabled anahtari kurali kapatiyor', () => {
    const id = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Toast');
    useAutomationStore.getState().setSelectedRuleId(id);
    render(<AutomationRuleDrawer />);

    fireEvent.click(screen.getByLabelText(/enabled/i));

    expect(useAutomationStore.getState().rules.find(r => r.id === id)?.enabled).toBe(false);
  });

  it('webhook basliklari satir satir ayristiriliyor, degerdeki iki nokta korunuyor', () => {
    const id = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Webhook');
    useAutomationStore.getState().setSelectedRuleId(id);
    render(<AutomationRuleDrawer />);

    const headers = screen.getByLabelText(/headers 1/i);
    fireEvent.change(headers, {
      // İkinci satır boş, üçüncüde ad yok, dördüncünün DEĞERİNDE iki nokta var.
      target: { value: 'Authorization: Bearer a:b\n\n: yalnizca-deger\nX-Source:namines' },
    });
    fireEvent.blur(headers);

    expect(useAutomationStore.getState().rules.find(r => r.id === id)?.actions[0].actionConfig.headers)
      .toEqual({ Authorization: 'Bearer a:b', 'X-Source': 'namines' });
  });

  it('tum basliklar silinince alan undefined oluyor', () => {
    // Bos nesne birakmak yapilandirma JSON'unda gereksiz "headers":{} birakirdi.
    const id = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Webhook');
    useAutomationStore.getState().setSelectedRuleId(id);
    render(<AutomationRuleDrawer />);

    const headers = screen.getByLabelText(/headers 1/i);
    fireEvent.change(headers, { target: { value: '' } });
    fireEvent.blur(headers);

    expect(useAutomationStore.getState().rules.find(r => r.id === id)?.actions[0].actionConfig.headers)
      .toBeUndefined();
  });

  it('Toast icin de mesaj alani sunuluyor', () => {
    // Bildirim metni sunucuya hic ugramiyor ama sablon ayni degiskenleri okuyor.
    const id = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Toast');
    useAutomationStore.getState().setSelectedRuleId(id);
    render(<AutomationRuleDrawer />);

    const message = screen.getByLabelText(/message 1/i);
    fireEvent.change(message, { target: { value: '{{tableName}} gitti' } });
    fireEvent.blur(message);

    expect(useAutomationStore.getState().rules.find(r => r.id === id)?.actions[0].actionConfig.message)
      .toBe('{{tableName}} gitti');
  });

  it('the close button clears the selection without deleting the rule', () => {
    const id = useAutomationStore.getState().addRule('t-orders', 'TableDeleted', 'Webhook');
    useAutomationStore.getState().setSelectedRuleId(id);

    render(<AutomationRuleDrawer />);
    fireEvent.click(screen.getByRole('button', { name: /close/i }));

    expect(useAutomationStore.getState().selectedRuleId).toBeNull();
    expect(useAutomationStore.getState().rules).toHaveLength(1);
  });
});
