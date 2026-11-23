/* =====================================================================
   Міграція 001 (приклад версіонування схеми MongoDB, додатково)
   Додає поле locale = "uk" відгукам, у яких його немає, і фіксує
   факт виконання в службовій колекції _migrations (як changelog у Mongock).
   Повторний запуск нічого не змінює.
   ===================================================================== */
db = db.getSiblingDB("cinema_reviews");
const version = "001_reviews_add_locale";

if (db.getCollection("_migrations").findOne({ _id: version })) {
  print(`${version}: уже застосовано — пропускаю`);
} else {
  const r = db.reviews.updateMany({ locale: { $exists: false } }, { $set: { locale: "uk" } });
  db.getCollection("_migrations").insertOne({ _id: version, appliedAt: new Date(), modified: r.modifiedCount });
  print(`${version}: оновлено ${r.modifiedCount} документів`);
}
