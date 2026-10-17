using System.ComponentModel;
using System.Windows.Controls;
using JKiller.Contracts;
using JKiller.Windows.Presentation;

namespace JKiller.App.Controls;

public sealed class AudioOutputPicker : ComboBox
{
    private BroadcasterModel? model;
    private bool synchronizing;

    public AudioOutputPicker()
    {
        DisplayMemberPath = nameof(AudioOutputDescriptor.DisplayName);
        DataContextChanged += (_, _) =>
        {
            if (model is not null) model.PropertyChanged -= ModelChanged;
            model = DataContext as BroadcasterModel;
            if (model is not null) model.PropertyChanged += ModelChanged;
            SynchronizeSelection();
        };
    }

    private void ModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(BroadcasterModel.AudioOutputs) or nameof(BroadcasterModel.Audio)) SynchronizeSelection();
    }

    private void SynchronizeSelection()
    {
        synchronizing = true;
        try
        {
            // Refresh can replace descriptor instances or remove the selected endpoint. Identity stays in Audio.
            ItemsSource = model?.AudioOutputs;
            SelectedItem = model?.Audio.Mode == AudioMode.SystemOutput
                ? model.AudioOutputs.FirstOrDefault(output => output.Id == model.Audio.RenderEndpointId) : null;
        }
        finally { synchronizing = false; }
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        if (!synchronizing && model is { Audio.Mode: AudioMode.SystemOutput } && SelectedItem is AudioOutputDescriptor output)
            model.SelectSystemAudio(output);
    }
}
