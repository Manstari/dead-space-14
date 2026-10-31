using Content.Shared.VendingMachines;
using Robust.Client.UserInterface;
using System.Linq;
using Content.Shared.VendingMachines.Components;
using Content.Client.DeadSpace.VendingMachines.UI;
using Content.Shared.DeadSpace.VendingMachines;

namespace Content.Client.VendingMachines;

public sealed class VendingMachineBoundUserInterface(EntityUid owner, Enum uiKey) : BoundUserInterface(owner, uiKey)
{
    [ViewVariables]
    private VendingMachineKeypadMenu? _keypadMenu;

    [ViewVariables]
    private List<VendingMachineInventoryEntry> _cachedInventory = new();

    protected override void Open()
    {
        base.Open();

        if (!EntMan.TryGetComponent(Owner, out VendingMachineKeypadComponent? keypad))
            keypad = new VendingMachineKeypadComponent();

        _keypadMenu = this.CreateWindowCenteredLeft<VendingMachineKeypadMenu>();
        _keypadMenu.Title = EntMan.GetComponent<MetaDataComponent>(Owner).EntityName;
        _keypadMenu.Configure(keypad.Columns, keypad.SpiralCapacity);
        _keypadMenu.OnCodeApproved += OnCodeApproved;
        Refresh();
    }

    public void Refresh()
    {
        var enabled = EntMan.TryGetComponent(Owner, out VendingMachineEjectComponent? eject) && !eject.Ejecting;

        var system = EntMan.System<VendingMachineSystem>();
        _cachedInventory = system.GetAllInventory(Owner);

        _keypadMenu?.Populate(_cachedInventory, enabled);
    }

    public void UpdateAmounts()
    {
        var enabled = EntMan.TryGetComponent(Owner, out VendingMachineEjectComponent? eject) && !eject.Ejecting;

        var system = EntMan.System<VendingMachineSystem>();
        _cachedInventory = system.GetAllInventory(Owner);
        _keypadMenu?.UpdateAmounts(_cachedInventory, enabled);
    }

    private void OnCodeApproved(int itemIndex)
    {
        var selectedItem = _cachedInventory.ElementAtOrDefault(itemIndex);
        if (selectedItem == null)
            return;

        SendPredictedMessage(new VendingMachineEjectMessage(selectedItem.Type, selectedItem.ID));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        if (_keypadMenu != null)
        {
            _keypadMenu.OnCodeApproved -= OnCodeApproved;
            _keypadMenu.Dispose();
        }
    }
}
