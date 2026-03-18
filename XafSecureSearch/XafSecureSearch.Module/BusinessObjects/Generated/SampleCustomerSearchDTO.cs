// Auto-generated search panel — exported from XafSecureSearch configuration
// Configuration: SampleCustomer
// Target Entity: XafSecureSearch.Module.BusinessObjects.SampleCustomer
// Generated: 2026-03-18 07:55:22 UTC
//
// To regenerate: open the SearchConfiguration and click 'Generate Source', then rebuild.

using System;
using System.ComponentModel;
using DevExpress.ExpressApp.DC;
using DevExpress.Persistent.Base;
using XafSecureSearch.Module.Attributes;

namespace XafSecureSearch.Module.BusinessObjects;

[DomainComponent]
[XafDisplayName("Search SampleCustomer")]
public class SampleCustomerSearchDTO
{
    [System.ComponentModel.DataAnnotations.Key]
    [Browsable(false)]
    public Guid Oid { get; set; } = Guid.NewGuid();

    [XafDisplayName("Age")]
    public int? Age { get; set; }

    [XafDisplayName("City")]
    [ToolTip("Supports wildcards: * (any chars), ? (single char)")]
    public string City { get; set; }

    [XafDisplayName("CreatedDate")]
    public DateTime? CreatedDate { get; set; }

    [XafDisplayName("Email")]
    [ToolTip("Supports wildcards: * (any chars), ? (single char)")]
    public string Email { get; set; }

    [XafDisplayName("IsActive")]
    public bool? IsActive { get; set; }

    [XafDisplayName("Name")]
    [ToolTip("Supports wildcards: * (any chars), ? (single char)")]
    public string Name { get; set; }

    public override string ToString() => "Search SampleCustomer";
}
