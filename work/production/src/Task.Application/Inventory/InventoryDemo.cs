namespace Task.Application.Inventory;

internal static class InventoryDemo
{
    public static InventoryData Create(DateOnly today)
    {
        var data = InventoryJson.Read(Json);
        var anchor = new DateOnly(2000, 1, 31);
        return data with
        {
            Movements = [.. data.Movements.Select(move => move with
        {
            Date = today.AddDays(move.Date.DayNumber - anchor.DayNumber),
        })]
        };
    }

    private const string Json = """
        {
        "version":1,
        "demo":true,
        "units":["шт.","уп.","г","кг","мл","л","шпр.","пар","набор","фл.","туб."],
        "suppliers":[
        {"id":"s1","name":"ДентМед Плюс"},
        {"id":"s2","name":"СтомСнаб"},
        {"id":"s3","name":"МедТрейд"},
        {"id":"s4","name":"ПрофиДент"},
        {"id":"s5","name":"DentalMarket"},
        {"id":"s6","name":"Дентал Сервис"}
        ],
        "materials":[
        {"id":"m1","name":"Пломбировочный композит","unit":"шпр.","threshold":5},
        {"id":"m2","name":"Алмазные боры","unit":"шт.","threshold":10},
        {"id":"m3","name":"Ватные палочки","unit":"уп.","threshold":10},
        {"id":"m4","name":"Перчатки нитриловые","unit":"уп.","threshold":4},
        {"id":"m5","name":"Травильный гель","unit":"мл","threshold":80},
        {"id":"m6","name":"Слюноотсосы","unit":"уп.","threshold":4},
        {"id":"m7","name":"Артикуляционная бумага","unit":"уп.","threshold":3},
        {"id":"m8","name":"Адгезив универсальный","unit":"фл.","threshold":3},
        {"id":"m9","name":"Коффердам латексный","unit":"уп.","threshold":3},
        {"id":"m10","name":"Иглы эндодонтические","unit":"уп.","threshold":2},
        {"id":"m11","name":"Файлы K-file №15","unit":"уп.","threshold":2},
        {"id":"m12","name":"Гуттаперчевые штифты","unit":"уп.","threshold":3},
        {"id":"m13","name":"Матрицы секционные","unit":"набор","threshold":2},
        {"id":"m14","name":"Клинья деревянные","unit":"уп.","threshold":3},
        {"id":"m15","name":"Ретракционная нить","unit":"уп.","threshold":2},
        {"id":"m16","name":"Полировочные диски","unit":"уп.","threshold":5},
        {"id":"m17","name":"Полировочная паста","unit":"туб.","threshold":2},
        {"id":"m18","name":"Цемент стеклоиономерный","unit":"набор","threshold":3},
        {"id":"m19","name":"Оттискная масса А-силикон","unit":"набор","threshold":2},
        {"id":"m20","name":"Дезинфицирующий раствор","unit":"л","threshold":6},
        {"id":"m21","name":"Одноразовые маски","unit":"уп.","threshold":4},
        {"id":"m22","name":"Пакеты для стерилизации","unit":"уп.","threshold":4},
        {"id":"m23","name":"Шприцы одноразовые","unit":"шт.","threshold":20},
        {"id":"m24","name":"Микробраши","unit":"уп.","threshold":5},
        {"id":"m25","name":"Шпатели стоматологические","unit":"шт.","threshold":5},
        {"id":"m26","name":"Временный пломбировочный материал","unit":"уп.","threshold":3},
        {"id":"m27","name":"Защитные очки","unit":"шт.","threshold":2}
        ],
        "movements":[
        {"id":"op1","type":"receipt","date":"2000-01-01","createdAt":1,"supplierId":"s1","invoice":"ДМ-2401","note":"Учебная накладная","lines":[{"id":"r1_0","materialId":"m1","quantity":12,"unitPrice":58},{"id":"r1_1","materialId":"m3","quantity":30,"unitPrice":3.2},{"id":"r1_2","materialId":"m4","quantity":15,"unitPrice":18},{"id":"r1_3","materialId":"m8","quantity":7,"unitPrice":55},{"id":"r1_4","materialId":"m24","quantity":18,"unitPrice":9}]},
        {"id":"op2","type":"receipt","date":"2000-01-03","createdAt":2,"supplierId":"s2","invoice":"СС-3107","note":"Учебная накладная","lines":[{"id":"r2_0","materialId":"m2","quantity":40,"unitPrice":3.8},{"id":"r2_1","materialId":"m3","quantity":25,"unitPrice":4.2},{"id":"r2_2","materialId":"m6","quantity":20,"unitPrice":8.5},{"id":"r2_3","materialId":"m14","quantity":10,"unitPrice":12}]},
        {"id":"op3","type":"receipt","date":"2000-01-05","createdAt":3,"supplierId":"s3","invoice":"МТ-0814","note":"Учебная накладная","lines":[{"id":"r3_0","materialId":"m5","quantity":500,"unitPrice":0.49},{"id":"r3_1","materialId":"m7","quantity":10,"unitPrice":6},{"id":"r3_2","materialId":"m20","quantity":30,"unitPrice":21},{"id":"r3_3","materialId":"m21","quantity":20,"unitPrice":12}]},
        {"id":"op4","type":"receipt","date":"2000-01-07","createdAt":4,"supplierId":"s4","invoice":"ПД-1523","note":"Учебная накладная","lines":[{"id":"r4_0","materialId":"m9","quantity":12,"unitPrice":24},{"id":"r4_1","materialId":"m10","quantity":8,"unitPrice":19},{"id":"r4_2","materialId":"m11","quantity":7,"unitPrice":29},{"id":"r4_3","materialId":"m12","quantity":8,"unitPrice":18}]},
        {"id":"op5","type":"receipt","date":"2000-01-09","createdAt":5,"supplierId":"s5","invoice":"DM-2205","note":"Учебная накладная","lines":[{"id":"r5_0","materialId":"m13","quantity":6,"unitPrice":87},{"id":"r5_1","materialId":"m15","quantity":9,"unitPrice":24},{"id":"r5_2","materialId":"m16","quantity":16,"unitPrice":22},{"id":"r5_3","materialId":"m17","quantity":6,"unitPrice":14}]},
        {"id":"op6","type":"receipt","date":"2000-01-11","createdAt":6,"supplierId":"s6","invoice":"ДС-1189","note":"Учебная накладная","lines":[{"id":"r6_0","materialId":"m18","quantity":7,"unitPrice":140},{"id":"r6_1","materialId":"m19","quantity":8,"unitPrice":164},{"id":"r6_2","materialId":"m22","quantity":22,"unitPrice":25},{"id":"r6_3","materialId":"m23","quantity":150,"unitPrice":0.25},{"id":"r6_4","materialId":"m25","quantity":35,"unitPrice":3.2},{"id":"r6_5","materialId":"m26","quantity":8,"unitPrice":33},{"id":"r6_6","materialId":"m27","quantity":3,"unitPrice":48}]},
        {"id":"op7","type":"receipt","date":"2000-01-13","createdAt":7,"supplierId":"s1","invoice":"ДМ-2417","note":"Учебная накладная","lines":[{"id":"r7_0","materialId":"m1","quantity":10,"unitPrice":54},{"id":"r7_1","materialId":"m2","quantity":15,"unitPrice":4.1},{"id":"r7_2","materialId":"m8","quantity":6,"unitPrice":52}]},
        {"id":"op8","type":"issue","date":"2000-01-14","createdAt":8,"note":"Расход стоматологических кабинетов","lines":[{"id":"i8_0","materialId":"m1","quantity":15,"supplierId":""},{"id":"i8_1","materialId":"m3","quantity":30,"supplierId":""},{"id":"i8_2","materialId":"m2","quantity":10,"supplierId":""},{"id":"i8_3","materialId":"m5","quantity":250,"supplierId":""}]},
        {"id":"op9","type":"receipt","date":"2000-01-15","createdAt":9,"supplierId":"s2","invoice":"СС-3130","note":"Учебная накладная","lines":[{"id":"r9_0","materialId":"m3","quantity":15,"unitPrice":3.9},{"id":"r9_1","materialId":"m4","quantity":10,"unitPrice":17.7},{"id":"r9_2","materialId":"m24","quantity":10,"unitPrice":8.5}]},
        {"id":"op10","type":"issue","date":"2000-01-16","createdAt":10,"note":"Расход стоматологических кабинетов","lines":[{"id":"i10_0","materialId":"m4","quantity":13,"supplierId":""},{"id":"i10_1","materialId":"m6","quantity":10,"supplierId":""},{"id":"i10_2","materialId":"m7","quantity":6,"supplierId":""},{"id":"i10_3","materialId":"m8","quantity":4,"supplierId":""}]},
        {"id":"op11","type":"receipt","date":"2000-01-17","createdAt":11,"supplierId":"s3","invoice":"МТ-0831","note":"Учебная накладная","lines":[{"id":"r11_0","materialId":"m5","quantity":300,"unitPrice":0.45},{"id":"r11_1","materialId":"m6","quantity":12,"unitPrice":8},{"id":"r11_2","materialId":"m21","quantity":10,"unitPrice":11.5}]},
        {"id":"op12","type":"issue","date":"2000-01-18","createdAt":12,"note":"Расход стоматологических кабинетов","lines":[{"id":"i12_0","materialId":"m9","quantity":4,"supplierId":""},{"id":"i12_1","materialId":"m10","quantity":6,"supplierId":""},{"id":"i12_2","materialId":"m11","quantity":3,"supplierId":""},{"id":"i12_3","materialId":"m12","quantity":8,"supplierId":""},{"id":"i12_4","materialId":"m14","quantity":3,"supplierId":""}]},
        {"id":"op13","type":"receipt","date":"2000-01-19","createdAt":13,"supplierId":"s4","invoice":"ПД-1540","note":"Учебная накладная","lines":[{"id":"r13_0","materialId":"m10","quantity":6,"unitPrice":21},{"id":"r13_1","materialId":"m11","quantity":4,"unitPrice":28},{"id":"r13_2","materialId":"m12","quantity":10,"unitPrice":17}]},
        {"id":"op14","type":"issue","date":"2000-01-20","createdAt":14,"note":"Расход стоматологических кабинетов","lines":[{"id":"i14_0","materialId":"m13","quantity":3,"supplierId":""},{"id":"i14_1","materialId":"m15","quantity":4,"supplierId":""},{"id":"i14_2","materialId":"m16","quantity":10,"supplierId":""},{"id":"i14_3","materialId":"m17","quantity":3,"supplierId":""}]},
        {"id":"op15","type":"receipt","date":"2000-01-21","createdAt":15,"supplierId":"s5","invoice":"DM-2233","note":"Учебная накладная","lines":[{"id":"r15_0","materialId":"m16","quantity":12,"unitPrice":20.5},{"id":"r15_1","materialId":"m17","quantity":4,"unitPrice":13},{"id":"r15_2","materialId":"m18","quantity":3,"unitPrice":133}]},
        {"id":"op16","type":"receipt","date":"2000-01-22","createdAt":16,"supplierId":"s6","invoice":"ДС-1210","note":"Учебная накладная","lines":[{"id":"r16_0","materialId":"m19","quantity":4,"unitPrice":159},{"id":"r16_1","materialId":"m20","quantity":12,"unitPrice":20},{"id":"r16_2","materialId":"m22","quantity":12,"unitPrice":24}]},
        {"id":"op17","type":"issue","date":"2000-01-23","createdAt":17,"note":"Расход стоматологических кабинетов","lines":[{"id":"i17_0","materialId":"m18","quantity":4,"supplierId":""},{"id":"i17_1","materialId":"m19","quantity":4,"supplierId":""},{"id":"i17_2","materialId":"m20","quantity":18,"supplierId":""},{"id":"i17_3","materialId":"m21","quantity":20,"supplierId":""}]},
        {"id":"op18","type":"receipt","date":"2000-01-24","createdAt":18,"supplierId":"s2","invoice":"СС-3151","note":"Учебная накладная","lines":[{"id":"r18_0","materialId":"m2","quantity":12,"unitPrice":4.5},{"id":"r18_1","materialId":"m3","quantity":10,"unitPrice":4.1},{"id":"r18_2","materialId":"m24","quantity":6,"unitPrice":8.9}]},
        {"id":"op19","type":"receipt","date":"2000-01-25","createdAt":19,"supplierId":"s3","invoice":"МТ-0848","note":"Учебная накладная","lines":[{"id":"r19_0","materialId":"m1","quantity":8,"unitPrice":50},{"id":"r19_1","materialId":"m5","quantity":120,"unitPrice":0.5}]},
        {"id":"op20","type":"issue","date":"2000-01-25","createdAt":20,"note":"Расход стоматологических кабинетов","lines":[{"id":"i20_0","materialId":"m22","quantity":12,"supplierId":""},{"id":"i20_1","materialId":"m23","quantity":60,"supplierId":""},{"id":"i20_2","materialId":"m24","quantity":12,"supplierId":""},{"id":"i20_3","materialId":"m25","quantity":15,"supplierId":""},{"id":"i20_4","materialId":"m26","quantity":6,"supplierId":""}]},
        {"id":"op21","type":"receipt","date":"2000-01-26","createdAt":21,"supplierId":"s5","invoice":"DM-2251","note":"Учебная накладная","lines":[{"id":"r21_0","materialId":"m13","quantity":4,"unitPrice":81},{"id":"r21_1","materialId":"m15","quantity":4,"unitPrice":22}]},
        {"id":"op22","type":"issue","date":"2000-01-27","createdAt":22,"note":"Расход стоматологических кабинетов","lines":[{"id":"i22_0","materialId":"m1","quantity":10,"supplierId":""},{"id":"i22_1","materialId":"m2","quantity":25,"supplierId":""},{"id":"i22_2","materialId":"m3","quantity":38,"supplierId":""},{"id":"i22_3","materialId":"m4","quantity":9,"supplierId":""},{"id":"i22_4","materialId":"m5","quantity":600,"supplierId":""}]},
        {"id":"op23","type":"issue","date":"2000-01-29","createdAt":23,"note":"Расход стоматологических кабинетов","lines":[{"id":"i23_0","materialId":"m6","quantity":15,"supplierId":""},{"id":"i23_1","materialId":"m7","quantity":4,"supplierId":""},{"id":"i23_2","materialId":"m8","quantity":7,"supplierId":""},{"id":"i23_3","materialId":"m9","quantity":2,"supplierId":""},{"id":"i23_4","materialId":"m10","quantity":1,"supplierId":""},{"id":"i23_5","materialId":"m11","quantity":2,"supplierId":""}]},
        {"id":"op24","type":"issue","date":"2000-01-30","createdAt":24,"note":"Расход стоматологических кабинетов","lines":[{"id":"i24_0","materialId":"m12","quantity":10,"supplierId":""},{"id":"i24_1","materialId":"m13","quantity":2,"supplierId":""},{"id":"i24_2","materialId":"m14","quantity":1,"supplierId":""},{"id":"i24_3","materialId":"m15","quantity":3,"supplierId":""},{"id":"i24_4","materialId":"m16","quantity":7,"supplierId":""},{"id":"i24_5","materialId":"m17","quantity":2,"supplierId":""},{"id":"i24_6","materialId":"m18","quantity":2,"supplierId":""},{"id":"i24_7","materialId":"m19","quantity":3,"supplierId":""},{"id":"i24_8","materialId":"m20","quantity":13,"supplierId":""}]},
        {"id":"op25","type":"issue","date":"2000-01-31","createdAt":25,"note":"Учебное списание · сегодня","lines":[{"id":"i25_0","materialId":"m21","quantity":2,"supplierId":""},{"id":"i25_1","materialId":"m22","quantity":9,"supplierId":""},{"id":"i25_2","materialId":"m23","quantity":30,"supplierId":""},{"id":"i25_3","materialId":"m24","quantity":10,"supplierId":""},{"id":"i25_4","materialId":"m25","quantity":9,"supplierId":""},{"id":"i25_5","materialId":"m26","quantity":2,"supplierId":""},{"id":"i25_6","materialId":"m27","quantity":2,"supplierId":""}]}
        ]
        }
        """;
}
