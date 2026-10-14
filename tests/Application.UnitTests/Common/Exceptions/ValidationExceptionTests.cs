using DevAgents.Application.Common.Exceptions;
using FluentValidation.Results;
using NUnit.Framework;
using Shouldly;

namespace DevAgents.Application.UnitTests.Common.Exceptions;

public class ValidationExceptionTests
{
    [Test]
    public void DefaultConstructorShouldCreateAnEmptyErrorDictionary()
    {
        var exception = new ValidationException();

        exception.Errors.ShouldBeEmpty();
        exception.Message.ShouldBe("Bir veya daha fazla doğrulama hatası oluştu.");
    }

    [Test]
    public void ShouldGroupFailuresByPropertyAndUseThemAsTheMessage()
    {
        var failures = new List<ValidationFailure>
        {
            new("Question", "Soru boş olamaz."),
            new("ProjectPath", "Proje klasörü bulunamadı: x"),
            new("ProjectPath", "Yol izin verilen kök dizinlerin dışında: y")
        };

        var exception = new ValidationException(failures);

        exception.Errors.Keys.ShouldBe(["Question", "ProjectPath"]);
        exception.Errors["ProjectPath"].ShouldBe(["Proje klasörü bulunamadı: x", "Yol izin verilen kök dizinlerin dışında: y"]);
        exception.Message.ShouldBe("Soru boş olamaz. Proje klasörü bulunamadı: x Yol izin verilen kök dizinlerin dışında: y");
    }
}
