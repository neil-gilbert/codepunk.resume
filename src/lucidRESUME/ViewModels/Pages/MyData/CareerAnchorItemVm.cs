using CommunityToolkit.Mvvm.ComponentModel;
using lucidRESUME.Core.Models.Resume;

namespace lucidRESUME.ViewModels.Pages.MyData;

public sealed partial class CareerAnchorItemVm : ObservableObject
{
    private readonly Action<WorkExperience, bool> _changed;

    public CareerAnchorItemVm(WorkExperience experience, Action<WorkExperience, bool> changed)
    {
        Experience = experience;
        _changed = changed;
        _isCareerAnchor = experience.IsCareerAnchor;
    }

    public WorkExperience Experience { get; }

    [ObservableProperty] private bool _isCareerAnchor;

    partial void OnIsCareerAnchorChanged(bool value) => _changed(Experience, value);
}
