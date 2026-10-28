namespace Ranvier.Css.View

open Ranvier.Css
open Ranvier.View
open Ranvier.View.Sugar

/// <summary>Compiles ordered utilities into attribute parts accepted by regular and void element constructors.</summary>
type StylesBuilder() =
    inherit UtilityBuilderBase()

    /// <summary>Applies fixed utilities using the element constructor's native and instance types.</summary>
    member _.Run<'native, 'instance>(values: StaticUtilityFragment) : Part<'native, 'instance> array =
        let style: Styled<'instance> = UtilityBuilder().Run values |> Utility.toStyled
        CssView.apply Builders.Attrs<'instance> style

    /// <summary>Applies precompiled conditional alternatives and tracked value slots as element attributes.</summary>
    member _.Run<'native, 'instance>(values: ReactiveUtilityFragment<'instance>) : Part<'native, 'instance> array =
        let style =
            UtilityBuilder().Run values
            |> Utility.toStyledReactive

        CssView.apply Builders.Attrs<'instance> style

/// <summary>Short style application syntax for element constructor attributes.</summary>
[<AutoOpen>]
module UtilityApplications =
    /// <summary>Composes utilities into one class writer and one tracked custom-property writer.</summary>
    let styles = StylesBuilder ()
