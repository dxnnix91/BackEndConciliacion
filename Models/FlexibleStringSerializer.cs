using System.Globalization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Backend.Models;

/// <summary>
/// Serializador tolerante para campos que en Mongo a veces vienen como texto (ej. "8433") y a
/// veces como número (ej. 8433), según cómo se haya insertado cada documento históricamente
/// (es el caso de "port" en la colección "connections"). Siempre deserializa a string en C#,
/// sin importar el BsonType real que traiga el documento.
/// </summary>
public class FlexibleStringSerializer : SerializerBase<string>
{
    public override string Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var reader = context.Reader;
        return reader.GetCurrentBsonType() switch
        {
            BsonType.String => reader.ReadString(),
            BsonType.Int32 => reader.ReadInt32().ToString(CultureInfo.InvariantCulture),
            BsonType.Int64 => reader.ReadInt64().ToString(CultureInfo.InvariantCulture),
            BsonType.Double => reader.ReadDouble().ToString(CultureInfo.InvariantCulture),
            BsonType.Null => ReadNullAsEmpty(reader),
            var otro => throw new FormatException($"No se pudo interpretar un valor de tipo {otro} como texto.")
        };
    }

    private static string ReadNullAsEmpty(MongoDB.Bson.IO.IBsonReader reader)
    {
        reader.ReadNull();
        return string.Empty;
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, string value)
    {
        context.Writer.WriteString(value ?? string.Empty);
    }
}