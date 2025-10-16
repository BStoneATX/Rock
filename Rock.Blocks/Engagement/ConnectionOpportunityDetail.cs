// <copyright>
// Copyright by the Spark Development Network
//
// Licensed under the Rock Community License (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.rockrms.com/license
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
// </copyright>
//

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data.Entity;
using System.Linq;

using Rock;
using Rock.Attribute;
using Rock.Constants;
using Rock.Data;
using Rock.Model;
using Rock.Security;
using Rock.ViewModels.Blocks;
using Rock.ViewModels.Blocks.Engagement.ConnectionOpportunityDetail;
using Rock.ViewModels.Utility;
using Rock.Web.Cache;

namespace Rock.Blocks.Engagement
{
    /// <summary>
    /// Displays the details of a particular connection opportunity.
    /// </summary>

    [DisplayName( "Connection Opportunity Detail" )]
    [Category( "Engagement" )]
    [Description( "Displays the details of a particular connection opportunity." )]
    [IconCssClass( "ti ti-question-mark" )]
    [SupportedSiteTypes( Model.SiteType.Web )]

    #region Block Attributes

    [BooleanField(
        "Show Edit",
        DefaultBooleanValue = true,
        Order = 2,
        Key = AttributeKey.ShowEdit )]

    #endregion

    [Rock.SystemGuid.EntityTypeGuid( "B51A4229-1C36-4D62-8EC6-97BB300BCDB2" )]
    [Rock.SystemGuid.BlockTypeGuid( "81567935-EFF8-4B19-B140-6F14FE0B896F" )]
    public class ConnectionOpportunityDetail : RockEntityDetailBlockType<ConnectionOpportunity, ConnectionOpportunityBag>
    {
        #region Keys

        private static class PageParameterKey
        {
            public const string ConnectionOpportunityId = "ConnectionOpportunityId";
            public const string ConnectionTypeId = "ConnectionTypeId";
        }

        private static class NavigationUrlKey
        {
            public const string ParentPage = "ParentPage";
        }

        #endregion Keys

        #region Fields

        private bool _hasAnyDefaultConnectors;

        #endregion

        #region Attribute Keys

        private static class AttributeKey
        {
            public const string ShowEdit = "ShowEdit";
        }

        #endregion

        #region Methods

        /// <inheritdoc/>
        public override object GetObsidianBlockInitialization()
        {
            var box = new DetailBlockBox<ConnectionOpportunityBag, ConnectionOpportunityDetailOptionsBag>();

            SetBoxInitialEntityState( box );

            box.NavigationUrls = GetBoxNavigationUrls();
            box.Options = GetBoxOptions( box.IsEditable );

            return box;
        }

        /// <summary>
        /// Gets the box options required for the component to render the view
        /// or edit the entity.
        /// </summary>
        /// <param name="isEditable"><c>true</c> if the entity is editable; otherwise <c>false</c>.</param>
        /// <returns>The options that provide additional details to the block.</returns>
        private ConnectionOpportunityDetailOptionsBag GetBoxOptions( bool isEditable )
        {
            var options = new ConnectionOpportunityDetailOptionsBag();
            options.IsReOrderColumnVisible = BlockCache.IsAuthorized( Authorization.EDIT, RequestContext.CurrentPerson );

            return options;
        }

        private List<Model.Attribute> GetConnectionRequestAttributes( string connectionOpportunityId )
        {
            return new AttributeService( RockContext ).GetByEntityTypeId( new ConnectionRequest().TypeId, true ).AsQueryable()
                .AsNoTracking()
                .Where( a =>
                    a.EntityTypeQualifierColumn.Equals( "ConnectionOpportunityId", StringComparison.OrdinalIgnoreCase ) &&
                    a.EntityTypeQualifierValue.Equals( connectionOpportunityId ) )
                .OrderBy( a => a.Order )
                .ThenBy( a => a.Name )
                .ToList();
        }

        private List<ListItemBag> GetConnectionOpportunityCampuses( int connectionOpportunityId )
        {
            var campusData = new ConnectionOpportunityCampusService( RockContext ).Queryable()
                .AsNoTracking()
                .Include( coc => coc.Campus )
                .Where( coc => coc.ConnectionOpportunityId == connectionOpportunityId )
                .Select( coc => new
                {
                    Campus = coc.Campus != null
                        ? new ListItemBag { Value = coc.Campus.Guid.ToString(), Text = coc.Campus.Name }
                        : null,
                    DefaultConnectorPersonAliasId = coc.DefaultConnectorPersonAliasId
                } )
                .ToList();

            _hasAnyDefaultConnectors = campusData.Any( c => c.DefaultConnectorPersonAliasId.HasValue );

            return campusData.Select( c => c.Campus ).Where( c => c != null ).ToList();
        }
        private Dictionary<string, ListItemBag> GetDefaultConnectors( int connectionOpportunityId )
        {
            var connectorGroupIds = new ConnectionOpportunityConnectorGroupService( RockContext )
                .Queryable()
                .Where( cg => cg.ConnectionOpportunityId == connectionOpportunityId )
                .Select( cg => cg.ConnectorGroupId )
                .ToList();

            var query = new ConnectionOpportunityCampusService( RockContext ).Queryable()
                .AsNoTracking()
                .Include( coc => coc.Campus )
                .Include( coc => coc.DefaultConnectorPersonAlias.Person )
                .Where( coc =>
                    coc.ConnectionOpportunityId == connectionOpportunityId &&
                    coc.DefaultConnectorPersonAliasId.HasValue &&
                    coc.DefaultConnectorPersonAlias.Person != null )
                .Join(
                    new GroupMemberService( RockContext ).Queryable(),
                    coc => coc.DefaultConnectorPersonAlias.PersonId,
                    gm => gm.PersonId,
                    ( coc, gm ) => new { coc, gm } )
                .Where( j =>
                    connectorGroupIds.Contains( j.gm.GroupId ) &&
                    j.gm.GroupMemberStatus == GroupMemberStatus.Active )
                .Select( j => new
                {
                    CampusGuid = j.coc.Campus.Guid,
                    AliasId = j.coc.DefaultConnectorPersonAlias.Id,
                    j.coc.DefaultConnectorPersonAlias.Person.NickName,
                    j.coc.DefaultConnectorPersonAlias.Person.FirstName,
                    j.coc.DefaultConnectorPersonAlias.Person.LastName
                } )
                .Distinct()
                .ToList()
                .ToDictionary(
                    k => k.CampusGuid.ToString(),
                    v => new ListItemBag
                    {
                        Value = v.AliasId.ToString(),
                        Text = $"{( v.NickName ?? v.FirstName ?? "" )} {v.LastName ?? ""}"
                    } );

            return query;
        }

        /// <summary>
        /// Saves the connection request attributes for this connection opportunity
        /// </summary>
        /// <param name="entityTypeId">The entity type identifier whose attributes are being edited.</param>
        /// <param name="qualifierColumn">The attribute qualifier column.</param>
        /// <param name="qualifierValue">The qualifier value.</param>
        /// <param name="viewStateAttributes">The attributes as edited in the UI.</param>
        private void SaveConnectionRequestAttributes( int entityTypeId, string qualifierColumn, string qualifierValue, List<PublicEditableAttributeBag> viewStateAttributes )
        {
            // Get the existing attributes for this entity type and qualifier value
            var attributeService = new AttributeService( RockContext );
            var attributes = attributeService.GetByEntityTypeQualifier( entityTypeId, qualifierColumn, qualifierValue, true ).ToList();

            // Delete any of those attributes that were removed in the UI
            var selectedAttributeGuids = viewStateAttributes.Select( a => a.Guid );
            foreach ( var attr in attributes.Where( a => !selectedAttributeGuids.Contains( a.Guid ) ) )
            {
                attributeService.Delete( attr );
                RockContext.SaveChanges();
            }

            // Update the Attributes that were assigned in the UI
            foreach ( var attributeState in viewStateAttributes )
            {
                Helper.SaveAttributeEdits( attributeState, entityTypeId, qualifierColumn, qualifierValue, RockContext );
            }
        }

        /// <summary>
        /// Validates the ConnectionOpportunity for any final information that might not be
        /// valid after storing all the data from the client.
        /// </summary>
        /// <param name="connectionOpportunity">The ConnectionOpportunity to be validated.</param>
        /// <param name="errorMessage">On <c>false</c> return, contains the error message.</param>
        /// <returns><c>true</c> if the ConnectionOpportunity is valid, <c>false</c> otherwise.</returns>
        private bool ValidateConnectionOpportunity( ConnectionOpportunity connectionOpportunity, out string errorMessage )
        {
            errorMessage = null;

            return true;
        }

        /// <summary>
        /// Sets the initial entity state of the box. Populates the Entity or
        /// ErrorMessage properties depending on the entity and permissions.
        /// </summary>
        /// <param name="box">The box to be populated.</param>
        private void SetBoxInitialEntityState( DetailBlockBox<ConnectionOpportunityBag, ConnectionOpportunityDetailOptionsBag> box )
        {
            var entity = GetInitialEntity();

            if ( entity == null )
            {
                box.ErrorMessage = $"The {ConnectionOpportunity.FriendlyTypeName} was not found.";
                return;
            }

            var isViewable = entity.IsAuthorized( Authorization.VIEW, RequestContext.CurrentPerson );
            box.IsEditable = entity.IsAuthorized( Authorization.EDIT, RequestContext.CurrentPerson );

            if ( entity.Id != 0 )
            {
                // Existing entity was found, prepare for view mode by default.
                if ( isViewable )
                {
                    box.Entity = GetEntityBagForView( entity );
                }
                else
                {
                    box.ErrorMessage = EditModeMessage.NotAuthorizedToView( ConnectionOpportunity.FriendlyTypeName );
                }
            }
            else
            {
                // New entity is being created, prepare for edit mode by default.
                if ( box.IsEditable )
                {
                    box.Entity = GetEntityBagForEdit( entity );
                }
                else
                {
                    box.ErrorMessage = EditModeMessage.NotAuthorizedToEdit( ConnectionOpportunity.FriendlyTypeName );
                }
            }

            PrepareDetailBox( box, entity );
        }

        /// <summary>
        /// Gets the entity bag that is common between both view and edit modes.
        /// </summary>
        /// <param name="entity">The entity to be represented as a bag.</param>
        /// <returns>A <see cref="ConnectionOpportunityBag"/> that represents the entity.</returns>
        private ConnectionOpportunityBag GetCommonEntityBag( ConnectionOpportunity entity )
        {
            if ( entity == null )
            {
                return null;
            }

            return new ConnectionOpportunityBag
            {
                IdKey = entity.IdKey,
                AdditionalSettingsJson = entity.AdditionalSettingsJson,
                Description = entity.Description,
                IconCssClass = entity.IconCssClass,
                IsActive = entity.IsActive,
                Name = entity.Name,
                Order = entity.Order,
                Photo = entity.Photo.ToListItemBag(),
                PublicName = entity.PublicName,
                ShowCampusOnTransfer = entity.ShowCampusOnTransfer,
                ShowConnectButton = entity.ShowConnectButton,
                ShowStatusOnTransfer = entity.ShowStatusOnTransfer,
                Summary = entity.Summary
            };
        }

        /// <inheritdoc/>
        protected override ConnectionOpportunityBag GetEntityBagForView( ConnectionOpportunity entity )
        {
            if ( entity == null )
            {
                return null;
            }

            var bag = GetCommonEntityBag( entity );

            if ( entity.Attributes == null )
            {
                entity.LoadAttributes( RockContext );
            }

            bag.LoadAttributesAndValuesForPublicView( entity, RequestContext.CurrentPerson, enforceSecurity: true );

            return bag;
        }

        //// <inheritdoc/>
        protected override ConnectionOpportunityBag GetEntityBagForEdit( ConnectionOpportunity entity )
        {
            if ( entity == null )
            {
                return null;
            }

            var bag = GetCommonEntityBag( entity );

            if ( entity.Attributes == null )
            {
                entity.LoadAttributes( RockContext );
            }

            bag.LoadAttributesAndValuesForPublicEdit( entity, RequestContext.CurrentPerson, enforceSecurity: true );

            bag.ConnectionRequestAttributes = GetConnectionRequestAttributes( entity.Id.ToString() ).ConvertAll( e => PublicAttributeHelper.GetPublicEditableAttribute( e ) );

            bag.Campuses = GetConnectionOpportunityCampuses( entity.Id );

            bag.PlacementGroupConfigs = new ConnectionOpportunityGroupConfigService( RockContext ).Queryable()
                .AsNoTracking()
                .Where( cfg => cfg.ConnectionOpportunityId == entity.Id )
                .OrderBy( cfg => cfg.GroupType.Name )
                .Select( cfg => new PlacementGroupConfigBag
                {
                    Guid = cfg.Guid,
                    GroupType = cfg.GroupType != null ? new ListItemBag { Value = cfg.GroupType.Guid.ToString(), Text = cfg.GroupType.Name } : null,
                    GroupMemberRole = cfg.GroupMemberRole != null ? new ListItemBag { Value = cfg.GroupMemberRole.Guid.ToString(), Text = cfg.GroupMemberRole.Name } : null,
                    GroupMemberStatus = ( int ) cfg.GroupMemberStatus,
                    UseAllGroupsOfType = cfg.UseAllGroupsOfType
                } )
                .ToList();

            bag.PlacementGroups = new ConnectionOpportunityGroupService( RockContext ).Queryable()
                .AsNoTracking()
                .Where( pg => pg.ConnectionOpportunityId == entity.Id )
                .OrderBy( pg => pg.Group.Name )
                .Select( pg => new PlacementGroupBag
                {
                    Guid = pg.Guid,
                    GroupType = pg.Group.GroupType != null ? new ListItemBag { Value = pg.Group.GroupType.Guid.ToString(), Text = pg.Group.GroupType.Name } : null,
                    Group = pg.Group != null ? new ListItemBag { Value = pg.Group.Guid.ToString(), Text = pg.Group.Name } : null
                } )
                .ToList();

            bag.ConnectorGroups = new ConnectionOpportunityConnectorGroupService( RockContext ).Queryable()
                .AsNoTracking()
                .Where( cg => cg.ConnectionOpportunityId == entity.Id )
                .OrderBy( cg => cg.Campus.Name )
                .ThenBy( cg => cg.ConnectorGroup.Name )
                .Select( cg => new ConnectorGroupBag
                {
                    Guid = cg.Guid,
                    ConnectorGroup = cg.ConnectorGroup != null ? new ListItemBag { Value = cg.ConnectorGroup.Guid.ToString(), Text = cg.ConnectorGroup.Name } : null,
                    Campus = cg.Campus != null ? new ListItemBag { Value = cg.Campus.Guid.ToString(), Text = cg.Campus.Name } : null
                } )
                .ToList();

            bag.DefaultConnectors = _hasAnyDefaultConnectors
                ? ( GetDefaultConnectors( entity.Id ) ?? new Dictionary<string, ListItemBag>() )
                : new Dictionary<string, ListItemBag>();

            var workflows = new ConnectionWorkflowService( RockContext ).Queryable()
                .AsNoTracking()
                .Where( wf => wf.ConnectionOpportunityId == entity.Id && wf.WorkflowTypeId.HasValue )
                .Select( wf => new
                {
                    wf.WorkflowTypeId,
                    Bag = new ConnectionWorkflowBag
                    {
                        Guid = wf.Guid,
                        WorkflowType = wf.WorkflowType != null ? new ListItemBag { Value = wf.WorkflowType.Guid.ToString(), Text = wf.WorkflowType.Name } : null,
                        TriggerType = ( int ) wf.TriggerType,
                        QualifierValue = wf.QualifierValue,
                        ManualTriggerFilterConnectionStatusId = wf.ManualTriggerFilterConnectionStatusId,
                        AppliesToAgeClassification = ( int ) wf.AppliesToAgeClassification,
                        IncludeDataViewId = wf.IncludeDataView != null ? new ListItemBag { Value = wf.IncludeDataView.Guid.ToString(), Text = wf.IncludeDataView.Name } : null,
                        ExcludeDataViewId = wf.ExcludeDataView != null ? new ListItemBag { Value = wf.ExcludeDataView.Guid.ToString(), Text = wf.ExcludeDataView.Name } : null
                    }
                } )
                .ToList();

            var workflowTypeOrder = entity.GetAdditionalSettingsOrNull<List<int>>( "WorkflowTypeOrder" );
            if ( workflowTypeOrder != null && workflowTypeOrder.Any() )
            {
                workflows = workflows
                    .OrderBy( wf =>
                    {
                        var id = wf.WorkflowTypeId ?? -1;
                        var idx = workflowTypeOrder.IndexOf( id );
                        return idx == -1 ? int.MaxValue : idx;
                    } )
                    .ToList();
            }

            if ( workflowTypeOrder == null || !workflowTypeOrder.Any() )
            {
                workflows = workflows.OrderBy( wf => wf.Bag.WorkflowType.Text ).ToList();
            }

            bag.ConnectionWorkflows = workflows
                .OrderBy( wf =>
                {
                    var id = wf.WorkflowTypeId ?? -1;
                    var idx = workflowTypeOrder?.IndexOf( id ) ?? -1;
                    return idx == -1 ? int.MaxValue : idx;
                } )
                .Select( wf => wf.Bag )
                .ToList();

            return bag;
        }

        /// <inheritdoc/>
        protected override bool UpdateEntityFromBox( ConnectionOpportunity entity, ValidPropertiesBox<ConnectionOpportunityBag> box )
        {
            if ( box.ValidProperties == null )
            {
                return false;
            }

            var connectionTypeId = PageParameter( PageParameterKey.ConnectionTypeId ).AsInteger();
            if ( connectionTypeId > 0 )
            {
                entity.ConnectionTypeId = connectionTypeId;
            }

            box.IfValidProperty( nameof( box.Bag.AdditionalSettingsJson ),
                () => entity.AdditionalSettingsJson = box.Bag.AdditionalSettingsJson );

            box.IfValidProperty( nameof( box.Bag.Description ),
                () => entity.Description = box.Bag.Description );

            box.IfValidProperty( nameof( box.Bag.IconCssClass ),
                () => entity.IconCssClass = box.Bag.IconCssClass );

            box.IfValidProperty( nameof( box.Bag.IsActive ),
                () => entity.IsActive = box.Bag.IsActive );

            box.IfValidProperty( nameof( box.Bag.Name ),
                () => entity.Name = box.Bag.Name );

            box.IfValidProperty( nameof( box.Bag.Order ),
                () => entity.Order = box.Bag.Order );

            box.IfValidProperty( nameof( box.Bag.Photo ),
                () => entity.PhotoId = box.Bag.Photo.GetEntityId<BinaryFile>( RockContext ) );

            box.IfValidProperty( nameof( box.Bag.PhotoId ),
                () => entity.PhotoId = box.Bag.PhotoId );

            box.IfValidProperty( nameof( box.Bag.PublicName ),
                () => entity.PublicName = box.Bag.PublicName );

            box.IfValidProperty( nameof( box.Bag.ShowCampusOnTransfer ),
                () => entity.ShowCampusOnTransfer = box.Bag.ShowCampusOnTransfer );

            box.IfValidProperty( nameof( box.Bag.ShowConnectButton ),
                () => entity.ShowConnectButton = box.Bag.ShowConnectButton );

            box.IfValidProperty( nameof( box.Bag.ShowStatusOnTransfer ),
                () => entity.ShowStatusOnTransfer = box.Bag.ShowStatusOnTransfer );

            box.IfValidProperty( nameof( box.Bag.Summary ),
                () => entity.Summary = box.Bag.Summary );

            box.IfValidProperty( nameof( box.Bag.AttributeValues ),
                () =>
                {
                    entity.LoadAttributes( RockContext );

                    entity.SetPublicAttributeValues( box.Bag.AttributeValues, RequestContext.CurrentPerson, enforceSecurity: true );
                } );

            return true;
        }

        /// <inheritdoc/>
        protected override ConnectionOpportunity GetInitialEntity()
        {
            return GetInitialEntity<ConnectionOpportunity, ConnectionOpportunityService>( RockContext, PageParameterKey.ConnectionOpportunityId );
        }

        /// <summary>
        /// Gets the box navigation URLs required for the page to operate.
        /// </summary>
        /// <returns>A dictionary of key names and URL values.</returns>
        private Dictionary<string, string> GetBoxNavigationUrls()
        {
            return new Dictionary<string, string>
            {
                [NavigationUrlKey.ParentPage] = this.GetParentPageUrl()
            };
        }

        /// <inheritdoc/>
        protected override bool TryGetEntityForEditAction( string idKey, out ConnectionOpportunity entity, out BlockActionResult error )
        {
            var entityService = new ConnectionOpportunityService( RockContext );
            error = null;

            // Determine if we are editing an existing entity or creating a new one.
            if ( idKey.IsNotNullOrWhiteSpace() )
            {
                // If editing an existing entity then load it and make sure it
                // was found and can still be edited.
                entity = entityService.Get( idKey, !PageCache.Layout.Site.DisablePredictableIds );
            }
            else
            {
                // Create a new entity.
                entity = new ConnectionOpportunity();
                entityService.Add( entity );

                var maxOrder = entityService.Queryable()
                    .Select( t => ( int? ) t.Order )
                    .Max();

                entity.Order = maxOrder.HasValue ? maxOrder.Value + 1 : 0;

                var connectionTypeId = PageParameter( PageParameterKey.ConnectionTypeId ).AsInteger();
                if ( connectionTypeId > 0 )
                {
                    entity.ConnectionTypeId = connectionTypeId;
                }
            }

            if ( entity == null )
            {
                error = ActionBadRequest( $"{ConnectionOpportunity.FriendlyTypeName} not found." );
                return false;
            }

            if ( !entity.IsAuthorized( Authorization.EDIT, RequestContext.CurrentPerson ) )
            {
                error = ActionBadRequest( $"Not authorized to edit ${ConnectionOpportunity.FriendlyTypeName}." );
                return false;
            }

            return true;
        }

        #endregion

        #region Block Actions

        /// <summary>
        /// Gets the box that will contain all the information needed to begin
        /// the edit operation.
        /// </summary>
        /// <param name="key">The identifier of the entity to be edited.</param>
        /// <returns>A box that contains the entity and any other information required.</returns>
        [BlockAction]
        public BlockActionResult Edit( string key )
        {
            if ( !TryGetEntityForEditAction( key, out var entity, out var actionError ) )
            {
                return actionError;
            }

            entity.LoadAttributes( RockContext );

            var bag = GetEntityBagForEdit( entity );

            return ActionOk( new ValidPropertiesBox<ConnectionOpportunityBag>
            {
                Bag = bag,
                ValidProperties = bag.GetType().GetProperties().Select( p => p.Name ).ToList()
            } );
        }

        /// <summary>
        /// Saves the entity contained in the box.
        /// </summary>
        /// <param name="box">The box that contains all the information required to save.</param>
        /// <returns>A new entity bag to be used when returning to view mode, or the URL to redirect to after creating a new entity.</returns>
        [BlockAction]
        public BlockActionResult Save( ValidPropertiesBox<ConnectionOpportunityBag> box )
        {
            var entityService = new ConnectionOpportunityService( RockContext );

            if ( !TryGetEntityForEditAction( box.Bag.IdKey, out var entity, out var actionError ) )
            {
                return actionError;
            }

            int? originalPhotoId = entity.PhotoId;

            // Update the entity instance from the information in the bag.
            if ( !UpdateEntityFromBox( entity, box ) )
            {
                return ActionBadRequest( "Invalid data." );
            }

            // Ensure everything is valid before saving.
            if ( !ValidateConnectionOpportunity( entity, out var validationMessage ) )
            {
                return ActionBadRequest( validationMessage );
            }

            var isNew = entity.Id == 0;

            RockContext.WrapTransaction( () =>
            {
                // Placement Group Configs
                {
                    var configService = new ConnectionOpportunityGroupConfigService( RockContext );
                    var existingConfigs = configService.Queryable().Where( c => c.ConnectionOpportunityId == entity.Id ).ToList();
                    var existingConfigByGuid = existingConfigs.ToDictionary( c => c.Guid );
                    var incomingConfigs = box.Bag.PlacementGroupConfigs ?? new List<PlacementGroupConfigBag>();
                    var incomingConfigGuids = incomingConfigs.Select( b => b.Guid ).ToHashSet();

                    foreach ( var config in existingConfigs.Where( c => !incomingConfigGuids.Contains( c.Guid ) ).ToList() )
                    {
                        configService.Delete( config );
                    }

                    foreach ( var cfgBag in incomingConfigs )
                    {
                        if ( !existingConfigByGuid.TryGetValue( cfgBag.Guid, out var cfg ) )
                        {
                            cfg = new ConnectionOpportunityGroupConfig { Guid = cfgBag.Guid == Guid.Empty ? Guid.NewGuid() : cfgBag.Guid };
                            configService.Add( cfg );
                        }

                        cfg.ConnectionOpportunityId = entity.Id;
                        cfg.GroupTypeId = cfgBag.GroupType.GetEntityId<GroupType>( RockContext ) ?? cfg.GroupTypeId;
                        cfg.GroupMemberRoleId = cfgBag.GroupMemberRole?.GetEntityId<GroupTypeRole>( RockContext );
                        cfg.GroupMemberStatus = ( GroupMemberStatus ) cfgBag.GroupMemberStatus;
                        cfg.UseAllGroupsOfType = cfgBag.UseAllGroupsOfType;
                    }
                }

                // Placement Groups
                {
                    var groupService = new ConnectionOpportunityGroupService( RockContext );
                    var existingGroups = groupService.Queryable().Where( g => g.ConnectionOpportunityId == entity.Id ).ToList();
                    var existingGroupByGuid = existingGroups.ToDictionary( g => g.Guid );
                    var incomingGroups = box.Bag.PlacementGroups ?? new List<PlacementGroupBag>();
                    var incomingGroupGuids = incomingGroups.Select( b => b.Guid ).ToHashSet();

                    foreach ( var og in existingGroups.Where( g => !incomingGroupGuids.Contains( g.Guid ) ).ToList() )
                    {
                        groupService.Delete( og );
                    }

                    foreach ( var pgBag in incomingGroups )
                    {
                        if ( !existingGroupByGuid.TryGetValue( pgBag.Guid, out var og ) )
                        {
                            og = new ConnectionOpportunityGroup { Guid = pgBag.Guid == Guid.Empty ? Guid.NewGuid() : pgBag.Guid };
                            groupService.Add( og );
                        }

                        og.ConnectionOpportunityId = entity.Id;
                        og.GroupId = pgBag.Group.GetEntityId<Rock.Model.Group>( RockContext ) ?? og.GroupId;
                    }
                }

                // Connector Groups
                {
                    var connGroupService = new ConnectionOpportunityConnectorGroupService( RockContext );
                    var existingConnGroups = connGroupService.Queryable().Where( g => g.ConnectionOpportunityId == entity.Id ).ToList();
                    var existingConnGroupByGuid = existingConnGroups.ToDictionary( g => g.Guid );
                    var incomingConnGroups = box.Bag.ConnectorGroups ?? new List<ConnectorGroupBag>();
                    var incomingConnGroupGuids = incomingConnGroups.Select( b => b.Guid ).ToHashSet();

                    foreach ( var cg in existingConnGroups.Where( g => !incomingConnGroupGuids.Contains( g.Guid ) ).ToList() )
                    {
                        connGroupService.Delete( cg );
                    }

                    foreach ( var cgBag in incomingConnGroups )
                    {
                        if ( !existingConnGroupByGuid.TryGetValue( cgBag.Guid, out var cg ) )
                        {
                            cg = new ConnectionOpportunityConnectorGroup { Guid = cgBag.Guid == Guid.Empty ? Guid.NewGuid() : cgBag.Guid };
                            connGroupService.Add( cg );
                        }

                        cg.ConnectionOpportunityId = entity.Id;
                        cg.ConnectorGroupId = cgBag.ConnectorGroup.GetEntityId<Rock.Model.Group>( RockContext ) ?? cg.ConnectorGroupId;
                        cg.CampusId = cgBag.Campus?.GetEntityId<Campus>( RockContext );
                    }
                }

                // Campuses
                {
                    var campusService = new ConnectionOpportunityCampusService( RockContext );
                    var existingCampuses = campusService.Queryable().Where( c => c.ConnectionOpportunityId == entity.Id ).ToList();
                    var existingCampusById = existingCampuses.ToDictionary( c => c.CampusId );
                    var incomingCampusIds = ( box.Bag.Campuses ?? new List<ListItemBag>() )
                        .Select( c => c.GetEntityId<Campus>( RockContext ) )
                        .Where( id => id.HasValue )
                        .Select( id => id.Value )
                        .ToHashSet();

                    foreach ( var ec in existingCampuses.Where( c => !incomingCampusIds.Contains( c.CampusId ) ).ToList() )
                    {
                        campusService.Delete( ec );
                    }

                    foreach ( var campusId in incomingCampusIds )
                    {
                        if ( !existingCampusById.TryGetValue( campusId, out var ec ) )
                        {
                            ec = new ConnectionOpportunityCampus { CampusId = campusId, ConnectionOpportunityId = entity.Id };
                            campusService.Add( ec );
                            existingCampuses.Add( ec );
                            existingCampusById[campusId] = ec;
                        }
                    }

                    var defaultConnectors = box.Bag.DefaultConnectors ?? new Dictionary<string, ListItemBag>();
                    if ( defaultConnectors.Any() )
                    {
                        var guids = defaultConnectors.Keys
                            .Select( k => k.AsGuidOrNull() )
                            .Where( g => g.HasValue )
                            .Select( g => g.Value )
                            .ToList();

                        var connectionOpportunityCampusById = new CampusService( RockContext )
                            .Queryable()
                            .AsNoTracking()
                            .Where( c => guids.Contains( c.Guid ) )
                            .Select( c => new { c.Guid, c.Id } )
                            .ToList()
                            .ToDictionary( x => x.Guid, x => x.Id );

                        foreach ( var kvp in defaultConnectors )
                        {
                            var campusGuid = kvp.Key.AsGuidOrNull();
                            if ( !campusGuid.HasValue )
                            {
                                continue;
                            }

                            if ( !connectionOpportunityCampusById.TryGetValue( campusGuid.Value, out var campusId ) )
                            {
                                continue;
                            }

                            if ( existingCampusById.TryGetValue( campusId, out var ec ) )
                            {
                                var personAliasId = kvp.Value?.Value.AsIntegerOrNull();
                                if ( personAliasId.HasValue )
                                {
                                    var connectorGroupIds = new ConnectionOpportunityConnectorGroupService( RockContext )
                                        .Queryable()
                                        .AsNoTracking()
                                        .Where( cg => cg.ConnectionOpportunityId == entity.Id )
                                        .Select( cg => cg.ConnectorGroupId )
                                        .ToList();

                                    // Validate that the default connector is an active member of a connector group
                                    var isValidConnector = new GroupMemberService( RockContext )
                                        .Queryable()
                                        .AsNoTracking()
                                        .Any( gm => gm.Person.PrimaryAliasId == personAliasId
                                                && connectorGroupIds.Contains( gm.GroupId )
                                                && gm.GroupMemberStatus == GroupMemberStatus.Active );

                                    if ( !isValidConnector )
                                    {
                                        continue;
                                    }
                                }
                                ec.DefaultConnectorPersonAliasId = personAliasId;
                            }
                        }
                    }
                }

                // Workflows
                {
                    var wfService = new ConnectionWorkflowService( RockContext );
                    var existingWfs = wfService.Queryable().Where( w => w.ConnectionOpportunityId == entity.Id ).ToList();
                    var existingWfByGuid = existingWfs.ToDictionary( w => w.Guid );
                    var incomingWfs = box.Bag.ConnectionWorkflows ?? new List<ConnectionWorkflowBag>();
                    var incomingWfGuids = incomingWfs.Select( b => b.Guid ).ToHashSet();

                    foreach ( var wf in existingWfs.Where( w => !incomingWfGuids.Contains( w.Guid ) ).ToList() )
                    {
                        wfService.Delete( wf );
                    }

                    foreach ( var wfBag in incomingWfs )
                    {
                        if ( !existingWfByGuid.TryGetValue( wfBag.Guid, out var wf ) )
                        {
                            wf = new ConnectionWorkflow { Guid = wfBag.Guid == Guid.Empty ? Guid.NewGuid() : wfBag.Guid };
                            wfService.Add( wf );
                        }

                        wf.ConnectionOpportunityId = entity.Id;
                        wf.WorkflowTypeId = wfBag.WorkflowType.GetEntityId<WorkflowType>( RockContext );
                        wf.TriggerType = ( ConnectionWorkflowTriggerType ) wfBag.TriggerType;
                        wf.QualifierValue = wfBag.QualifierValue;
                        wf.ManualTriggerFilterConnectionStatusId = wfBag.ManualTriggerFilterConnectionStatusId;
                        wf.AppliesToAgeClassification = ( AppliesToAgeClassification ) wfBag.AppliesToAgeClassification;
                        wf.IncludeDataViewId = wfBag.IncludeDataViewId.GetEntityId<DataView>( RockContext );
                        wf.ExcludeDataViewId = wfBag.ExcludeDataViewId.GetEntityId<DataView>( RockContext );
                    }

                    // Persist workflow order in AdditionalSettingsJson column
                    var orderedIds = incomingWfs
                        .Select( b => b.WorkflowType.GetEntityId<WorkflowType>( RockContext ) )
                        .Where( id => id.HasValue )
                        .Select( id => id.Value )
                        .ToList();
                    entity.SetAdditionalSettings( "WorkflowTypeOrder", orderedIds );
                }

                RockContext.SaveChanges();
                entity.SaveAttributeValues( RockContext );

                // Delete orphaned previous photo if it changed.
                if ( originalPhotoId.HasValue && originalPhotoId != entity.PhotoId )
                {
                    var binaryFileService = new BinaryFileService( RockContext );
                    var oldPhoto = binaryFileService.Get( originalPhotoId.Value );
                    if ( oldPhoto != null )
                    {
                        string errorMessage;
                        if ( binaryFileService.CanDelete( oldPhoto, out errorMessage ) )
                        {
                            binaryFileService.Delete( oldPhoto );
                            RockContext.SaveChanges();
                        }
                    }
                }
            } );

            // Save Connection Request Attributes
            var connectionRequestAttributes = box.Bag.ConnectionRequestAttributes;
            if ( connectionRequestAttributes != null )
            {
                SaveConnectionRequestAttributes( new ConnectionRequest().TypeId, "ConnectionOpportunityId", entity.Id.ToString(), connectionRequestAttributes );
            }

            if ( isNew )
            {
                return ActionContent( System.Net.HttpStatusCode.Created, this.GetCurrentPageUrl( new Dictionary<string, string>
                {
                    [PageParameterKey.ConnectionOpportunityId] = entity.IdKey
                } ) );
            }

            // Ensure navigation properties will work now.
            entity = entityService.Get( entity.Id );
            entity.LoadAttributes( RockContext );

            var bag = GetEntityBagForEdit( entity );

            return ActionOk( new ValidPropertiesBox<ConnectionOpportunityBag>
            {
                Bag = bag,
                ValidProperties = bag.GetType().GetProperties().Select( p => p.Name ).ToList()
            } );
        }

        /// <summary>
        /// Deletes the specified entity.
        /// </summary>
        /// <param name="key">The identifier of the entity to be deleted.</param>
        /// <returns>A string that contains the URL to be redirected to on success.</returns>
        [BlockAction]
        public BlockActionResult Delete( string key )
        {
            var entityService = new ConnectionOpportunityService( RockContext );

            if ( !TryGetEntityForEditAction( key, out var entity, out var actionError ) )
            {
                return actionError;
            }

            if ( !entityService.CanDelete( entity, out var errorMessage ) )
            {
                return ActionBadRequest( errorMessage );
            }

            entityService.Delete( entity );
            RockContext.SaveChanges();

            return ActionOk( this.GetParentPageUrl() );
        }

        /// <summary>
        /// Changes the ordered position of a single step type attribute.
        /// </summary>
        /// <param name = "key" > The identifier of the step type attribute that will be moved.</param>
        /// <param name = "beforeKey" > The identifier of the step type attribute it will be placed before.</param>
        /// <returns>An empty result that indicates if the operation succeeded.</returns>
        [BlockAction]
        public BlockActionResult ReorderConnectionRequestAttribute( string key, string beforeKey )
        {
            var connectionOpportunity = new ConnectionOpportunityService( RockContext ).Get( PageParameter( PageParameterKey.ConnectionOpportunityId ), !PageCache.Layout.Site.DisablePredictableIds );
            if ( connectionOpportunity == null )
            {
                return ActionBadRequest( "Connection opportunity not found." );
            }

            var items = GetConnectionRequestAttributes( connectionOpportunity.Id.ToString() );

            if ( !items.ReorderEntity( key, beforeKey ) )
            {
                return ActionBadRequest( "Invalid reorder attempt." );
            }

            RockContext.SaveChanges();
            return ActionOk();
        }

        [BlockAction]
        public BlockActionResult ReorderConnectionWorkflow( string key, string beforeKey )
        {
            var connectionOpportunity = new ConnectionOpportunityService( RockContext ).Get( PageParameter( PageParameterKey.ConnectionOpportunityId ), !PageCache.Layout.Site.DisablePredictableIds );
            if ( connectionOpportunity == null )
            {
                return ActionBadRequest( "Connection opportunity not found." );
            }

            if ( !Guid.TryParse( key, out var movedGuid ) )
            {
                return ActionBadRequest( "Invalid workflow key." );
            }

            Guid? beforeGuid = beforeKey.IsNotNullOrWhiteSpace() && Guid.TryParse( beforeKey, out var parsed ) ? parsed : ( Guid? ) null;
            if ( beforeKey.IsNotNullOrWhiteSpace() && !beforeGuid.HasValue )
            {
                return ActionBadRequest( "Invalid before key." );
            }

            var workflows = new ConnectionWorkflowService( RockContext ).Queryable()
                .AsNoTracking()
                .Where( wf => wf.ConnectionOpportunityId == connectionOpportunity.Id )
                .Select( wf => new { wf.Guid, wf.WorkflowTypeId } )
                .ToList();

            var moved = workflows.FirstOrDefault( wf => wf.Guid == movedGuid );
            if ( moved == null || !moved.WorkflowTypeId.HasValue )
            {
                return ActionBadRequest( "Workflow not found." );
            }

            var savedOrder = connectionOpportunity.GetAdditionalSettingsOrNull<List<int>>( "WorkflowTypeOrder" ) ?? new List<int>();
            var orderedIds = workflows
                .Where( w => w.WorkflowTypeId.HasValue )
                .Select( w => w.WorkflowTypeId.Value )
                .OrderBy( id =>
                {
                    var idx = savedOrder.IndexOf( id );
                    return idx == -1 ? int.MaxValue : idx;
                } )
                .ToList();

            // Move the requested workflow before the target, or to the end.
            var movedId = moved.WorkflowTypeId.Value;
            orderedIds.Remove( movedId );
            if ( beforeGuid.HasValue )
            {
                var before = workflows.FirstOrDefault( wf => wf.Guid == beforeGuid.Value );
                var insertIndex = before?.WorkflowTypeId.HasValue == true ? orderedIds.IndexOf( before.WorkflowTypeId.Value ) : -1;
                if ( insertIndex >= 0 )
                {
                    orderedIds.Insert( insertIndex, movedId );
                }
                else
                {
                    orderedIds.Add( movedId );
                }
            }
            else
            {
                orderedIds.Add( movedId );
            }

            connectionOpportunity.SetAdditionalSettings( "WorkflowTypeOrder", orderedIds );

            RockContext.SaveChanges();
            return ActionOk();
        }

        [BlockAction]
        public BlockActionResult GetConnectionWorkflowQualifierOptions( int triggerType )
        {
            var connectionTypeId = PageParameter( PageParameterKey.ConnectionTypeId ).AsInteger();
            var qualifierOptions = new List<ListItemBag>();

            var theTriggerType = ( ConnectionWorkflowTriggerType ) triggerType;

            switch ( theTriggerType )
            {
                case ConnectionWorkflowTriggerType.StatusChanged:
                // we don't use the QualifierValue DB column for manual trigger type, but we need the connection status options
                // to populate the dropdown for ManualTriggerFilterConnectionStatusId
                case ConnectionWorkflowTriggerType.Manual:
                    qualifierOptions = new ConnectionStatusService( RockContext )
                        .Queryable()
                        .AsNoTracking()
                        .Where( stat => stat.ConnectionTypeId == connectionTypeId || stat.ConnectionTypeId == null )
                        .OrderBy( stat => stat.Name )
                        .Select( stat => new ListItemBag { Text = stat.Name, Value = stat.Guid.ToString() } )
                        .ToList();

                    break;

                case ConnectionWorkflowTriggerType.StateChanged:
                    qualifierOptions = typeof( ConnectionState ).ToEnumListItemBag().ToList();
                    var connectionType = new ConnectionTypeService( RockContext ).Get( connectionTypeId );
                    if ( connectionType != null && !connectionType.EnableFutureFollowup )
                    {
                        var futureFollowUp = ( ( int ) ConnectionState.FutureFollowUp ).ToString();
                        qualifierOptions = qualifierOptions.Where( o => o.Value != futureFollowUp ).ToList();
                    }

                    break;

                case ConnectionWorkflowTriggerType.ActivityAdded:
                    qualifierOptions = new ConnectionActivityTypeService( RockContext )
                        .Queryable()
                        .AsNoTracking()
                        .Where( a => a.ConnectionTypeId == connectionTypeId )
                        .OrderBy( a => a.Name )
                        .Select( a => new ListItemBag { Text = a.Name, Value = a.Guid.ToString() } )
                        .ToList();

                    break;
            }

            return ActionOk( new { qualifierOptions } );
        }

        [BlockAction]
        public BlockActionResult GetDefaultConnectorOptions( DefaultConnectorOptionsRequestBag bag )
        {
            if ( bag.GroupGuids == null || !bag.GroupGuids.Any() )
            {
                return ActionOk( new List<ListItemBag>() );
            }

            var validGroupGuids = bag.GroupGuids
                .Where( g => Guid.TryParse( g, out _ ) )
                .Select( Guid.Parse )
                .ToList();

            if ( !validGroupGuids.Any() )
            {
                return ActionOk( new List<ListItemBag>() );
            }

            var groupService = new GroupService( RockContext );
            var groupIds = groupService
                .Queryable()
                .AsNoTracking()
                .Where( g => validGroupGuids.Contains( g.Guid ) && g.IsActive )
                .Select( g => g.Id )
                .ToList();

            if ( !groupIds.Any() )
            {
                return ActionOk( new List<ListItemBag>() );
            }

            var connectionOpportunityId = PageParameter( PageParameterKey.ConnectionOpportunityId ).AsInteger();

            var defaultConnectorOptions = new GroupMemberService( RockContext )
                .Queryable()
                .AsNoTracking()
                .Include( gm => gm.Person )
                .Where( gm =>
                    groupIds.Contains( gm.GroupId ) &&
                    gm.GroupMemberStatus == GroupMemberStatus.Active &&
                    gm.Person != null )
                .Select( gm => new ListItemBag
                {
                    Value = gm.Person.PrimaryAliasId.ToString(),
                    Text = ( gm.Person.NickName ?? gm.Person.FirstName ?? "" ) + " " + ( gm.Person.LastName ?? "" )
                } )
                .DistinctBy( p => p.Value )
                .ToList();

            return ActionOk( defaultConnectorOptions );
        }

        #endregion
    }
}
