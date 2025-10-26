namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using Xunit;

    public class CssObjectSizingTests : CssConstructionFunctions
    {
        [Fact]
        public void CssObjectFitNoneLegal()
        {
            var snippet = "object-fit : none";
            var property = ParseDeclaration(snippet);
            Assert.Equal("object-fit", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ObjectFitProperty>(property);
            var concrete = (ObjectFitProperty)property;
            Assert.False(property.IsAnimatable);
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("none", concrete.Value);
            //Assert.Equal(ObjectFitting.None, concrete.Fitting);
        }

        [Fact]
        public void ObjectFitScaledownIllegal()
        {
            var snippet = "object-fit : scaledown";
            var property = ParseDeclaration(snippet);
            Assert.Equal("object-fit", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ObjectFitProperty>(property);
            var concrete = (ObjectFitProperty)property;
            Assert.False(property.IsAnimatable);
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
            //Assert.Equal(ObjectFitting.Fill, concrete.Fitting);
        }

        [Fact]
        public void ObjectFitScaleDownLegal()
        {
            var snippet = "object-fit : scale-DOWN";
            var property = ParseDeclaration(snippet);
            Assert.Equal("object-fit", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ObjectFitProperty>(property);
            var concrete = (ObjectFitProperty)property;
            Assert.False(property.IsAnimatable);
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("scale-down", concrete.Value);
            //Assert.Equal(ObjectFitting.ScaleDown, concrete.Fitting);
        }

        [Fact]
        public void CssObjectFitCoverLegal()
        {
            var snippet = "object-fit : cover";
            var property = ParseDeclaration(snippet);
            Assert.Equal("object-fit", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ObjectFitProperty>(property);
            var concrete = (ObjectFitProperty)property;
            Assert.False(property.IsAnimatable);
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("cover", concrete.Value);
            //Assert.Equal(ObjectFitting.Cover, concrete.Fitting);
        }

        [Fact]
        public void CssObjectFitContainLegal()
        {
            var snippet = "object-fit : contain";
            var property = ParseDeclaration(snippet);
            Assert.Equal("object-fit", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ObjectFitProperty>(property);
            var concrete = (ObjectFitProperty)property;
            Assert.False(property.IsAnimatable);
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("contain", concrete.Value);
            //Assert.Equal(ObjectFitting.Contain, concrete.Fitting);
        }

        [Fact]
        public void CssObjectPositionCenterLegal()
        {
            var snippet = "object-position : center";
            var property = ParseDeclaration(snippet);
            Assert.Equal("object-position", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ObjectPositionProperty>(property);
            var concrete = (ObjectPositionProperty)property;
            Assert.True(property.IsAnimatable);
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("center", concrete.Value);
        }

        [Fact]
        public void ObjectPositionTopLeftIllegal()
        {
            var snippet = "object-position : top-left";
            var property = ParseDeclaration(snippet);
            Assert.Equal("object-position", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ObjectPositionProperty>(property);
            var concrete = (ObjectPositionProperty)property;
            Assert.True(property.IsAnimatable);
            Assert.False(concrete.IsInherited);
            Assert.False(concrete.HasValue);
        }

        [Fact]
        public void ObjectPositionTopLeftLegal()
        {
            var snippet = "object-position : top left";
            var property = ParseDeclaration(snippet);
            Assert.Equal("object-position", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ObjectPositionProperty>(property);
            var concrete = (ObjectPositionProperty)property;
            Assert.True(property.IsAnimatable);
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("top left", concrete.Value);
        }

        [Fact]
        public void CssObjectPosition5050Legal()
        {
            var snippet = "object-position : 50%   50% ";
            var property = ParseDeclaration(snippet);
            Assert.Equal("object-position", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ObjectPositionProperty>(property);
            var concrete = (ObjectPositionProperty)property;
            Assert.True(property.IsAnimatable);
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("50% 50%", concrete.Value);
        }

        [Fact]
        public void CssObjectPositionLeft30Legal()
        {
            var snippet = "object-position : left  30px";
            var property = ParseDeclaration(snippet);
            Assert.Equal("object-position", property.Name);
            Assert.False(property.IsImportant);
            Assert.IsType<ObjectPositionProperty>(property);
            var concrete = (ObjectPositionProperty)property;
            Assert.True(property.IsAnimatable);
            Assert.False(concrete.IsInherited);
            Assert.True(concrete.HasValue);
            Assert.Equal("left 30px", concrete.Value);
        }
    }
}







